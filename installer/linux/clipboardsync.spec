# Only the version is filled in by installer/linux/build-rpm.sh; everything else is fixed.
%global debug_package %{nil}
# The published executable is already a self-contained, single-file binary.
%global __os_install_post %{nil}

Name:           clipboardsync
Version:        @VERSION@
Release:        1%{?dist}
Summary:        Clipboard sharing between your own devices

License:        MIT
URL:            https://github.com/T2PeNBiX99wcoxKv3A4g/ClipboardSync
Source0:        %{name}-%{version}.tar.gz

BuildArch:      x86_64

# What the self-contained .NET runtime and the embedded Photino browser need at run time.
Requires:       gtk3
Requires:       webkit2gtk4.1
Requires:       libnotify
Requires:       libicu
Requires:       openssl-libs
Requires:       zlib
Requires:       krb5-libs
Requires:       fontconfig
Requires:       freetype

%description
ClipboardSync keeps one clipboard board across the devices you pair. Copying
text, a URL or an image on one device sends it to the others over an encrypted
peer-to-peer session, with no account and no server in the middle.

%prep
%autosetup -n %{name}-%{version}

%install
install -Dpm 0755 ClipboardSync %{buildroot}/opt/ClipboardSync/ClipboardSync
install -Dpm 0644 clipboardsync.desktop %{buildroot}%{_datadir}/applications/clipboardsync.desktop
install -Dpm 0644 clipboardsync.png %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/clipboardsync.png

mkdir -p %{buildroot}%{_bindir}
ln -s /opt/ClipboardSync/ClipboardSync %{buildroot}%{_bindir}/clipboardsync

%post
# The desktop entry and the icon are new; refresh the caches that hold them.
update-desktop-database >/dev/null 2>&1 || :
gtk-update-icon-cache --quiet %{_datadir}/icons/hicolor >/dev/null 2>&1 || :
touch --no-create %{_datadir}/icons/hicolor >/dev/null 2>&1 || :

%postun
update-desktop-database >/dev/null 2>&1 || :
gtk-update-icon-cache --quiet %{_datadir}/icons/hicolor >/dev/null 2>&1 || :
touch --no-create %{_datadir}/icons/hicolor >/dev/null 2>&1 || :

%files
/opt/ClipboardSync/ClipboardSync
%{_bindir}/clipboardsync
%{_datadir}/applications/clipboardsync.desktop
%{_datadir}/icons/hicolor/256x256/apps/clipboardsync.png

%changelog
* Tue Nov 03 2026 yukiyosanako <yukiyosanako@users.noreply.github.com> - @VERSION@-1
- Package the published ClipboardSync build.
