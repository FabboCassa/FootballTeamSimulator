# TEMPLATE for tools\keystore.local.ps1 (Roadmap 10.2).
#
# Copy this file to tools\keystore.local.ps1 and fill in the real values. That filename is
# GITIGNORED - the passwords must never reach the repository. tools\release-android.ps1
# dot-sources it and hands the values to Unity as environment variables.
#
#   Copy-Item tools\keystore.local.example.ps1 tools\keystore.local.ps1
#
# Create the keystore once with keytool (ships with the JDK in Unity's Android module):
#
#   keytool -genkeypair -v -keystore fts-release.keystore -alias fts -keyalg RSA -keysize 2048 -validity 10000
#
# Then back the .keystore file up somewhere off this machine. Losing it means losing the
# ability to update the app under the same signing identity (unless Play App Signing is on).

$env:FTS_ANDROID_KEYSTORE      = "C:\path\to\fts-release.keystore"
$env:FTS_ANDROID_KEYSTORE_PASS = "CHANGE_ME"
$env:FTS_ANDROID_KEYALIAS      = "fts"
$env:FTS_ANDROID_KEYALIAS_PASS = "CHANGE_ME"
