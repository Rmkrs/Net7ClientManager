# Bundled Forge data

This directory contains the exact current stable Net7 Forge navigation package
used as the offline and first-install baseline.

Do not rebuild or edit the package in this repository. Refreshing the baseline
is a separate Net7 Client Manager release operation:

```powershell
.\Refresh-BundledNavigationData.ps1 `
    -PackagePath <published-forge-package> `
    -ExpectedRevision <revision> `
    -ExpectedSha256 <sha256>
```

The script validates the immutable full package and copies its bytes exactly.
A newer valid dataset already downloaded by a user is never replaced by an
older bundled baseline.
