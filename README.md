# AR Storybook

A mobile augmented reality companion app for a physical children's picture book, built in Unity. Scanning each page with a phone camera brings that page's scene to life through a short interactive moment. Designed for children roughly 6 to 8 years old.

## Overview

AR Storybook pairs a printed storybook with a phone app. Each page in the book carries a printed image that the app recognizes through the camera. Recognizing a page spawns 3D content anchored to it (an island, a character, a Phoenix) and runs a short interaction specific to that page: popping clouds, catching falling drops by tilting the phone, saying a phrase out loud, holding down on a shape, tapping floating orbs, dragging collected rewards onto a Phoenix, or trying on an AR crown through the front camera. Completing most pages awards a colored "Spark," visualized by a pendant gemstone changing color, building toward a final reveal on the last page.

Key design decisions:

- **One feature active at a time.** A single shared lock (`AppStateManager`) ensures only one page's AR feature can run at once, so scanning a second page mid-interaction is ignored rather than interrupting the first.
- **Islands spawn unparented from the tracked marker**, after a short pose-stabilization delay. Attaching 3D content directly to a live-tracked image causes it to drift and wobble as tracking refines; capturing the pose once, after it settles, keeps the content stable in place.
- **Page 12 (the AR crown / face filter) runs in its own scene, reached by a full process restart**, not a scene reload. ARCore's native session state does not reliably survive a same-process switch from world tracking to front-facing face tracking; restarting the whole app process guarantees every AR session is the first and only one in its process.

## Built With

- Unity 6000.4.5f1
- Universal Render Pipeline 17.4.0
- AR Foundation 6.4.3, with AR Core XR Plugin 6.4.3 (Android) and AR Kit XR Plugin 6.4.3
- XR Interaction Toolkit 3.4.1
- Input System 1.19.0
- TextMeshPro
- NativeGallery (yasirkula) for saving photos to the device gallery
- A native Android/iOS Speech-to-Text plugin (`Assets/Plugins/SpeechToText`) for the page 6 voice interaction

## Prerequisites

- Unity Hub with Editor version **6000.4.5f1** installed, including the Android Build Support module (SDK, NDK, and OpenJDK components)
- An Android device running **Android 11 (API 30) or later** with ARCore support, connected over USB with developer mode and USB debugging enabled
- For the page 12 AR crown feature specifically, the device must support ARCore's front-facing camera (face tracking), not just rear-camera ARCore. Check the device against Google's [ARCore supported devices list](https://developers.google.com/ar/devices) before relying on that page, front-camera support is a smaller subset of the devices that support ARCore at all, so a device can pass everything else in this list and still not run page 12
- The physical storybook, or printed copies of the page images in `Assets/Images` (`page1.png`, `page4.png`, `page6.png`, `page8.png`, `page10.png`, `page11.png`, `page12.png`), since the app has nothing to scan without them

Verify the Android device is detected:

```
adb devices
```

## Installation

1. Clone the repository:
   ```
   git clone <repository-url>
   cd AR_Storybook
   ```
2. Open Unity Hub, choose **Add project from disk**, and select the cloned folder. Unity Hub will prompt to install Editor version 6000.4.5f1 if it is not already present.
3. Once the project finishes importing, open `Assets/Scenes/Bootstrap.unity`.
4. Open **File > Build Settings** and confirm the scene list contains, in order: `Bootstrap`, `Ar1`, `Ar2`. (`SampleScene` and `Setup` should stay unchecked.)
5. Switch platform to **Android** if it is not already selected, then **Switch Platform**.
6. Under **Edit > Project Settings > Player > Android**, confirm the application identifier is set and the minimum API level is 30 or higher.
7. Connect the Android device and click **Build And Run**.

## Usage

**First launch:**
1. Tap **Start** on the main menu.
2. Enter a name, then continue past the welcome and instructions screens.
3. Grant the camera, microphone, and photo/gallery permission prompts when they appear, these are required for scanning, the page 6 voice interaction, and saving crown selfies.

**Playing (with the storybook in front of the camera):**
1. Point the phone camera at any page listed below. The scene for that page appears once it's recognized.
2. Follow the on-screen prompt for that page: tap, tilt, hold, speak, or drag, as described per page.
3. A **Back To Menu** button is always available on screen to return to the main menu, and a page-specific cancel button appears during an active interaction.
4. Sparks collected during a session reset each time **Start** is pressed again, so a new playthrough always begins fresh.

**What happens on each page:**

| Page | Feature Name | Description |
|---|---|---|
| Page 1 | Gloomy Beginning | Tap the clouds to clear the grey sky and reveal the island |
| Page 4 | Blue Spark | Tilt the phone to slide a catch zone and collect ten falling water drops |
| Page 6 | Red Spark | Say a calming phrase out loud; each word lights up as it's recognised until the whole phrase is complete |
| Page 8 | Yellow Spark | Hold down on a shadow until it fully shrinks and morphs into a square |
| Page 10 | Gold Spark | Tap each of the ten drifting orbs to collect it and fill the meter, earning the Gold Spark once all ten are gathered |
| Page 11 | The Phoenix | Drag each earned spark onto the Phoenix for a rainbow finale |
| Page 12 | AR Crown | Use a face filter to see yourself wearing a crown and take a selfie |

Photos taken on page 12 can be viewed afterward from the **Gallery** button on the main menu.

Page 12 only works on devices whose front-facing camera supports ARCore face tracking, see the [Prerequisites](#prerequisites) note above. On an unsupported device, every other page still works normally.

## Project Structure

```
Assets/
  Scripts/     Gameplay and app logic (one AR tracker + completion sequence per page)
  Scenes/      Bootstrap (entry point), Ar1 (main story/world tracking), Ar2 (face filter)
  Images/      Printed page marker images used by the AR reference image library
  Plugins/     Native Speech-to-Text bridge (Android/iOS)
  Shaders/     Custom URP shaders (e.g. the stylized cloud surface)
```

`Bootstrap` is a dedicated, AR-component-free scene that decides whether to load `Ar1` (normal launch) or `Ar2` (after a page-12-triggered process restart), so a fresh AR session is never created before that decision is made.
