# Automatic deployment maintenance — Bright Packages

Use either existing Visual Studio Web Deploy profile as usual:
- site99797-WebDeploy: webpos.runasp.net (testing)
- site69540-WebDeploy: possolution.runasp.net (client)

The profiles now import Deployment/Maintenance.targets. The old generic AppOffline rule is intentionally disabled because these targets own the branded page's lifecycle.

Automatic sequence:
1. Build the cleanup helper locally before taking the site offline.
2. Upload only Deployment/OfflinePayload/app_offline.htm into the selected site's root using Web Deploy, preserving other files.
3. Publish the normal application while that page remains visible.
4. After a successful publish, remove only the site's root app_offline.htm. The application starts again on the next request.

The offline payload and local cleanup helper are explicitly excluded from the normal application publish package. Do not copy the offline payload into a normal folder/FTP deployment; those workflows do not run these Web Deploy targets. No database migration is required.

If publishing fails or is cancelled after the offline upload, maintenance intentionally remains visible. Finish a successful publish to restore the site. If necessary, verify the application upload completed, then remove only app_offline.htm from the website root using the hosting file manager. Keep all other files.

Open-page behavior: once a browser loads the updated scripts, a same-origin deployment/unavailable response shows an in-page notice. Entered fields remain in the page; Check again probes availability and Continue dismisses the notice. Writes are never automatically retried. Older pages that were already open before this script's first deployment need a normal reload to receive this behavior.

Local publishing prerequisites: Microsoft Web Deploy V3 and .NET Framework 4.8 targeting files (available on this development computer). The helper uses the installed Microsoft Web Deploy library; credentials remain in the publishing process and helper environment, not in command text or new credential files. Certificate validation is not disabled.

Validation completed locally:
- Native Web Deploy/MSBuild upload smoke test and managed cleanup removed only app_offline.htm, preserving keep.txt.
- Synthetic credential escaping test covered semicolon, percent, apostrophe and dollar characters.
- Both profiles evaluated with the automatic hooks and preservation settings.
- File-system publish succeeded and contains no root app_offline.htm, Deployment folder or cleanup executable.
- Browser checked branded screen and interrupted-form recovery with entries retained.

The hosted Web Deploy sequence has not been executed in this change. Verify the complete lifecycle during the next webpos publish before publishing to the client website.

Microsoft IIS background: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/app-offline

## Saved publish credentials
The maintenance upload explicitly resolves the encrypted password from the selected local publish profile, including when Visual Studio disables the standard GetPassword step. If no saved credential is available, publishing stops before contacting the server with instructions to save the Web Deploy credentials in profile settings. Passwords are not written into the project source.
