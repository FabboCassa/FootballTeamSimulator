# Store asset specifications

Every image each storefront demands, at the size it demands it. Checked against the
stores' own requirements in August 2026 — these change (Apple in particular retires
screenshot sizes as devices change), so re-check the linked pages on a submission day
rather than trusting this file blindly.

A note that saves a rejection: **no real club, competition, sponsor or player names or
logos in any of these images.** The game generates its own identities precisely so this
is never a problem; a screenshot that happens to show a recognisable trademark undoes
that.

---

## Where the files go

`store-assets/` at the repository root, in the layout `tools\preflight-store.ps1` expects — it reads
each PNG's header and checks the dimensions and the alpha channel against the tables below, so a
misnamed or mis-sized file is caught here rather than by a store reviewer:

```
store-assets/
  play/      icon-512.png (alpha)  feature-1024x500.png (NO alpha)  phone/ 2-8 at 1080x1920
  appstore/  icon-1024.png (NO alpha)                               iphone/ 3-10 at 1320x2868
  steam/     capsule-small-231x87.png  capsule-header-460x215.png  capsule-main-616x353.png
             capsule-vertical-374x448.png  library-600x900.png  library-hero-3840x1240.png
             library-logo-1280x720.png (alpha)  community-184x184.png  screenshots/ 5+ at 1920x1080
  web/       favicon-32.png  favicon-180.png  og-1200x630.png
```

PNG throughout: the checker reads dimensions out of the PNG header, and for a JPEG it can only warn.
Apple rejects an image one pixel off outright, so that warning is not a small thing.

---

## Steam

| Asset | Size (px) | Notes |
|---|---|---|
| Small capsule | 231 x 87 | Search results. The name must be legible at this size — this is the one that usually fails. |
| Header capsule | 460 x 215 | Store page top, wishlists, daily deals. |
| Main capsule | 616 x 353 | Front-page features and sales. |
| Vertical capsule | 374 x 448 | Seasonal sale pages. |
| Page background | 1438 x 810 | Optional; Steam blurs and tints it. |
| Library capsule | 600 x 900 | The player's own library. Portrait. |
| Library header | 460 x 215 | Library list view. |
| Library hero | 3840 x 1240 | Library detail page banner. |
| Library logo | 1280 x 720 | Transparent PNG, sits over the hero. |
| Community icon | 184 x 184 | |
| Screenshots | 1920 x 1080 recommended | At least 5. Steam shows the first ones largest. |

Formats: PNG or JPG. The library logo needs transparency; the capsules do not.

Suggested screenshot set (5 minimum, all from the actual game):
1. The pitch editor with the eleven placed — this is the screen that explains the game.
2. A match in progress with the score HUD.
3. The league table.
4. A transfer negotiation mid-counteroffer.
5. The ranked ladder or a private-league lobby.
6. The club finances / facilities screen.

---

## Google Play

| Asset | Size (px) | Format | Required |
|---|---|---|---|
| App icon | 512 x 512 | 32-bit PNG with alpha, max 1 MB | Yes |
| Feature graphic | 1024 x 500 | JPEG or 24-bit PNG, **no** alpha | Yes — publishing is blocked without it |
| Phone screenshots | 1080 x 1920 (9:16) | JPEG or 24-bit PNG, no alpha | Yes, 2 to 8 |
| 7" tablet screenshots | e.g. 1200 x 1920 | same | Only if tablet support is claimed |
| 10" tablet screenshots | e.g. 1600 x 2560 | same | Only if tablet support is claimed |

Constraints on screenshots: each side between 320 and 3840 px, aspect ratio no wider
than 2:1. Portrait is the natural fit — the game's portrait layout (6.4) is the one to
capture.

Text limits: title 30 characters, short description 80, full description 4000. All three
are drafted in `store-copy.md`.

---

## App Store

| Asset | Size (px) | Notes |
|---|---|---|
| App icon | 1024 x 1024 | No alpha, no rounded corners — Apple rounds it. |
| iPhone 6.9" screenshots | 1320 x 2868 portrait | The only iPhone size that must be supplied; Apple scales it down for smaller devices. |
| iPad 13" screenshots | 2064 x 2752 portrait | Only if the app is offered on iPad. |

sRGB PNG or JPEG, no transparency, and the dimensions are validated **to the pixel** — an
image one pixel off is rejected outright. 3 to 10 screenshots per device family.

Text limits: name 30 characters, subtitle 30, promotional text 170, keywords 100
(comma-separated, no spaces), description 4000.

---

## Web (Cloudflare Pages)

Not a store, but the page needs the same care:

| Asset | Size (px) | Notes |
|---|---|---|
| Favicon | 32 x 32 and 180 x 180 | The 180 is the iOS home-screen icon. |
| Open Graph image | 1200 x 630 | What appears when the link is shared. |

The WebGL template (`client/Assets/WebGLTemplates/FTS/index.html`) currently draws its
loading crest in CSS/SVG and references no image files at all, which is why the build has
no favicon yet. Adding one means dropping the files into the template folder and adding
the `<link rel="icon">` tags.

---

## Producing the screenshots

The game runs at any resolution, so the cleanest route is the desktop standalone with the
window sized to the target aspect ratio, or the WebGL build in a browser window sized the
same way. On Windows, `Win + Shift + S` or Steam's own F12 capture both work; for exact
pixel dimensions, resize the window with a script or use the browser devtools' device
toolbar, which sets an exact viewport.

For the mobile portrait shots, `adb exec-out screencap -p > shot.png` on a connected
device gives the device's native resolution, which is already the right shape.

---

## Sources

- [Add preview assets to showcase your app — Play Console Help](https://support.google.com/googleplay/android-developer/answer/9866151?hl=en)
- [App Store screenshot sizes 2026](https://appdrift.co/guides/app-store-connect/screenshot-requirements)
- [Google Play screenshot and feature graphic sizes 2026](https://www.choicely.com/tutorials/google-play-app-store-guidelines-screenshots-listings)
