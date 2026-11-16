import {renderSite} from "@photino-net-utils/site";
import {site} from "../site.config.mjs";
import {icons} from "../site.icons";
import {copy, languages} from "./i18n";
import {docs} from "./docs.generated";

renderSite({config: {...site, icons}, languages, defaultLanguage: "en", copy, docs});
