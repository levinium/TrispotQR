## A test build for Mac

This is Trispot QR on a Mac for the first time. It is built from the same code as the Windows release and passes the same automated tests, but it has not yet been used by a person on a real Mac. That is what this build is for. If something looks wrong or does not work, please open an issue and say which Mac and which macOS version you have.

You need macOS 14 (Sonoma) or later.

## Which file

- **TrispotQR-mac-arm64.zip** for a Mac with Apple silicon (M1 or later). If the Apple menu's **About This Mac** shows a chip name starting with "Apple M", this is yours.
- **TrispotQR-mac-x64.zip** for an older Mac with an Intel processor.

Each has a `.sha256` checksum beside it.

## Opening it the first time

The app is not signed by an Apple developer account, so macOS refuses it at first. You only need to allow it once.

- Unzip the file and drag **Trispot QR** into **Applications**.
- Open it. macOS says it cannot verify the app. Choose **Done**, not Move to Trash.
- Open **System Settings**, then **Privacy & Security**, scroll down, and choose **Open Anyway** next to the message about Trispot QR. Confirm with your password.
- It opens normally from then on.

## What to know

- It does not update itself on a Mac. When a newer version exists, the notice offers **Download** and opens the release page instead.
- Settings and saved styles are kept in your user Library, apart from any Windows copy.

Things worth trying: saving PNG and SVG files, copying a code and pasting it into Pages, Keynote or Mail, adding a logo, switching between light and dark mode, and scanning a saved code with a phone.
