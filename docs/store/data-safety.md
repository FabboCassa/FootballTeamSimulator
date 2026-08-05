# Data collection — the answers to the store questionnaires

Google Play calls it the Data Safety form, Apple calls it App Privacy. Both ask the same
question — what leaves the device and why — and both treat a wrong answer as a policy
violation rather than a mistake. What follows is derived from what the server code
actually stores, not from intent.

## The short version

The single-player career collects nothing. It runs offline, and the save file
(`career.sav`) never leaves the device — in the browser it lives in IndexedDB, on desktop
and mobile in the app's own storage.

Everything below applies **only if the player creates an account** to use private leagues
or the ranked ladder.

## What is collected, and where it lives

| Data | Where it comes from | Stored as | Why |
|---|---|---|---|
| Email address | Registration | Plain, in `users` | Account identity and sign-in |
| Password | Registration | **Hashed** by ASP.NET Core Identity, never plain | Sign-in |
| Display name | Registration | Plain, in `coach_profiles` | Shown to other players in leagues and on the leaderboard |
| Push notification token | Device registration, if the player allows notifications | Plain, in `device_registrations` | Sending "you were outbid" / "your match is about to start" |
| IP address | Every ranked request | **Salted SHA-256 hash only** — the raw address is never written | Detecting one person running several accounts in the same group |
| Device identifier | Optional `X-Fts-Device` header | **Salted SHA-256 hash only** | Same |
| Game data | Play | Plain | Squads, transfers, results, ratings, palmares |
| Reports and integrity flags | Player reports and automatic checks | Plain | Reviewing suspected collusion or abuse |

Rotating `Integrity:SignalSalt` makes the entire history of hashed addresses
uncorrelatable, which is the point of hashing them rather than storing them.

## What is NOT collected

No advertising identifier. No analytics SDK. No location. No contacts, photos, microphone
or camera. No payment information — there are no purchases. No third-party ad or
attribution network of any kind.

## Third parties

| Service | What it receives | Why |
|---|---|---|
| Google Firebase Cloud Messaging | The device push token and the message text | Delivering push notifications |

That is the whole list. If FCM is left disabled (`Fcm:Enabled=false`) there are no third
parties at all.

## Form answers

**Google Play Data Safety**

- Does your app collect or share any of the required user data types? **Yes.**
- Personal info → Email address: collected, not shared, required, used for *Account
  management*.
- Personal info → Name (display name): collected, not shared, optional, *Account
  management* and *App functionality*.
- App activity → Other user-generated content: collected, not shared, *App functionality*.
- Device or other IDs: collected (hashed), not shared, *Fraud prevention, security and
  compliance*.
- Is all data encrypted in transit? **Yes** — provided the deployment terminates TLS.
  See the note below; this must be true before the form is submitted.
- Can users request that data be deleted? **Yes** — in-app (Account → Delete account) and
  through the public page at `/delete-account.html` on the game's website.

**Apple App Privacy**

- Data used to track you: **none.**
- Data linked to you: Contact Info (email, name), User Content (game data), Identifiers
  (hashed device/IP).
- Data not linked to you: none.

## Account deletion (Roadmap 10.2a) — how to describe it on the forms

`POST /auth/account/delete` (authenticated, password re-confirmed) deletes the login, the
coach profile, every session and device registration, the private-league memberships and
the hashed integrity signals; the review-queue flags stop naming anyone. The ranked record
(`ranked_coaches` + the append-only `ranked_awards`) is **kept but re-stamped with a random
id that belongs to nobody**, so other coaches' final tables and honours stay correct, and
the ladder seat is freed to play on as an AI club. Deletion is immediate — there is no
grace period to explain on the forms.

Two routes, as the stores require: **in-app** (Account → Delete account) and the **public
page** `web/delete-account.html`, published at `<site>/delete-account.html`, which signs in
and then calls the same endpoint. Give that URL as the "account deletion URL" on Play.

## One blocker that must be closed before submission

1. **TLS.** "Encrypted in transit" is an answer the deployment has to earn. The docker
   compose stack serves plain HTTP on 8080 for local development; production needs HTTPS
   in front of the API (a reverse proxy or the hosting platform's own termination) before
   the Data Safety form can honestly claim it.
