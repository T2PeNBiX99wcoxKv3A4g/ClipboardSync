using ClipboardSync.Messaging;
using ClipboardSync.Models;
using ClipboardSync.Security;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>
///     Runs the pairing flow in both directions. The initiator shows the code the peer's challenge
///     produced; the target raises a prompt and only pins the peer after the local user accepts.
/// </summary>
internal sealed class PairingService : Singleton<PairingService>
{
    private readonly Lock _incomingLock = new();
    private readonly SemaphoreSlim _outgoing = new(1, 1);
    private IncomingPairing? _incoming;

    public PairingService() => NativeBridge.MessageReceived += HandleMessageAsync;

    private Task<NativeResponse?> HandleMessageAsync(NativeContext context) => context.Request.Type switch
    {
        "pair.start" => StartAsync(context),
        "pair.respond" => RespondAsync(context),
        _ => Task.FromResult<NativeResponse?>(null)
    };

    /// <summary>Called by the listener; shows the prompt and waits for the user's answer.</summary>
    public async Task<bool> RequestApprovalAsync(PairingPrompt prompt, CancellationToken token)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_incomingLock)
        {
            if (_incoming is not null)
                return false;

            _incoming = new(requestId, prompt, decision);
        }

        WindowManager.Instance.RunOnMainWindow(() => WindowManager.Instance.ShowMainWindow("devices"));
        WindowManager.Instance.NotifyMainWindow("pair.request",
            new PairRequestPayload(requestId, prompt.DeviceId, prompt.DeviceName, prompt.Code));

        try
        {
            return await decision.Task.WaitAsync(PeerProtocol.PairTimeout, token);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            lock (_incomingLock)
                if (_incoming?.RequestId == requestId)
                    _incoming = null;
        }
    }

    /// <summary>Called after either side stored a new peer.</summary>
    public void OnPeerPaired(PairedDevice peer)
    {
        Log.Information("Device {Name} ({Id}) is paired", peer.Name, peer.Id);
        SyncService.Instance.OnPeerPaired(peer);
    }

    private Task<NativeResponse?> StartAsync(NativeContext context)
    {
        if (context.Payload<PairStartRequest>() is not { Host.Length: > 0 } request)
            return Task.FromResult<NativeResponse?>(context.Fail("pair.start.result", "A device address is required."));

        _ = Task.Run(() => RunAsync(request));
        return Task.FromResult<NativeResponse?>(context.Reply("pair.start.result", new OkPayload()));
    }

    private async Task RunAsync(PairStartRequest request)
    {
        if (!await _outgoing.WaitAsync(0))
        {
            Push("pair.result", new PairResultPayload(false, null, null, "Another pairing is already in progress."));
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(PeerProtocol.PairTimeout + PeerProtocol.ConnectionTimeout);

            var peer = await PeerClient.PairAsync(request.Host, request.Port,
                SettingsService.Instance.Current.DeviceName,
                (code, device) =>
                {
                    Push("pair.code", new PairCodePayload(code, device.Id, device.Name));
                    return Task.CompletedTask;
                }, cts.Token);

            await PeerStore.Instance.AddOrUpdateAsync(peer);
            Push("pair.result", new PairResultPayload(true, peer.Id, peer.Name, null));
            OnPeerPaired(peer);
        }
        catch (Exception ex)
        {
            Log.Information(ex, "Pairing with {Host}:{Port} failed", request.Host, request.Port);
            Push("pair.result", new PairResultPayload(false, null, null, ex.Message));
        }
        finally
        {
            _outgoing.Release();
        }
    }

    private Task<NativeResponse?> RespondAsync(NativeContext context)
    {
        if (context.Payload<PairRespondRequest>() is not { RequestId.Length: > 0 } request)
            return Task.FromResult<NativeResponse?>(context.Fail("pair.respond.result", "The request id is missing."));

        IncomingPairing? incoming;
        lock (_incomingLock)
            incoming = _incoming?.RequestId == request.RequestId ? _incoming : null;

        incoming?.Decision.TrySetResult(request.Accept);
        return Task.FromResult<NativeResponse?>(context.Reply("pair.respond.result", new OkPayload()));
    }

    private static void Push(string type, object payload) =>
        WindowManager.Instance.NotifyMainWindow(type, payload);

    private sealed record IncomingPairing(
        string RequestId,
        PairingPrompt Prompt,
        TaskCompletionSource<bool> Decision);
}