# Bigger Spray Mod for R.E.P.O.

A customizable spray mod for R.E.P.O. that allows players to place custom images throughout the game world.

## Features

- **Custom Spray Images**: Use your own PNG/JPG images as sprays (GIF support coming soon)
- **Adjustable Size**: Scale your sprays from tiny to massive
- **Multiple Spray Support**: Cycle through multiple spray images in-game
- **Network Synchronization**: All players see each other's sprays
- **Scale Preview**: See exactly how big your spray will be before placing it
- **Host Customization**: Server hosts can control spray lifetime and maximum count

## Installation

1. Ensure you have [BepInEx](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/) installed
2. Make sure you have [REPOLib](https://thunderstore.io/c/repo/p/Zehs/REPOLib/) installed
3. Download the latest release from the [Thunderstore](https://github.com/your-username/BiggerSprayMod/releases) page
4. Extract the contents to your R.E.P.O. game directory
5. The mod will be installed to `BepInEx/plugins/BiggerSprayMod`

## Adding Your Own Sprays

1. Navigate to `BepInEx/config/BiggerSprayImages/`
2. Place your PNG or JPG images in this folder
3. In-game, open the mod config and click "Refresh Sprays"
4. Your images should now be available to select

**Note**: For best performance, keep your spray images under 5MB and use PNG format for transparency support.

## Controls

| Key         | Action                                |
|-------------|---------------------------------------|
| F           | Place spray                           |
| G           | Toggle between static and gif sprays  |
| Left Arrow  | Previous spray image                  |
| Right Arrow | Next spray image                      |
| Left Alt    | Hold to preview spray size/position   |
| +           | Increase spray size                   |
| -           | Decrease spray size                   |
| Mouse Wheel | Adjust spray size (when previewing)   |

All keybindings can be changed in the mod config file.

## Usage
1. Press the spray key (default: F) to place a spray
2. Use the previous/next keys (default: Left/Right arrows) to cycle through your sprays
3. Hold the scale preview key (default: Left Alt) to see the size of your spray before placing it (optionally scroll or use +/- keys to adjust size)
4. Press the toggle key (default: G) to toggle between static and gif sprays
5. Use chat commands to manage sprays:
   - `/addspray SPRAYNAME`: Add a new spray image from clipboard
   - `/addgifspray GIFSPRAYNAME`: Add a new gif spray from clipboard

## Configuration

The mod creates a config file at `BepInEx/config/MishaOpstal.BigSprayMod.cfg` with the following settings:

### Spray Settings
- **Spray Key**: Key used to place a spray (Default: F)
- **Previous/Next Spray Key**: Keys to cycle through your sprays (Default: Q/E)
- **Show spray if it exceeds the size limit locally**: Whether to show large sprays on your end (Default: true)
- **Selected Spray Image**: Currently selected spray image

### Scale Settings
- **Scale Preview Key**: Key to hold for previewing spray size (Default: Left Alt)
- **Increase/Decrease Scale Key**: Keys to adjust spray size (Default: +/-)
- **Use Scroll Wheel**: Enable scroll wheel for size adjustment (Default: true)
- **Spray Scale**: Default spray size multiplier (Default: 1.0)
- **Minimum/Maximum Scale**: Limits for spray sizing (Default: 0.1-5.0)
- **Scale Speed**: How quickly the scale changes (Default: 0.1)
- **Scale Preview Color**: Color of the preview overlay (Default: Semi-transparent green)

### Host Settings
- **Spray Lifetime**: How long sprays last in seconds (Default: 60s, 0 = permanent)
- **Max Sprays**: Maximum number of sprays before oldest ones are removed (Default: 10)

## Networking

- When joining a server, the mod automatically synchronizes with the host's settings
- Sprays are compressed before sending to reduce network traffic
- Extremely large images (>5MB) may not be sent over the network

## Compatibility

- Requires R.E.P.O. version 1.x or higher
- Requires BepInEx 5.x or higher
- Requires REPOLib

## Known Issues

- Very large spray images may cause performance issues
- If you experience lag when placing sprays, try using smaller image files
- Transparent PNGs work best with this mod

## License

Includes mgGif by Gwaredd Mountain, licensed under the MIT License. See [gif/mgGif_LICENSE](https://github.com/OnTheLink/BiggerSprayMod/blob/main/gif/mgGif_LICENSE).

## Credits

- Developed by MishaOpstal
- Original mod by [IvitoDev](https://thunderstore.io/c/repo/p/IvitoDev/SprayMod)
- Special thanks to the R.E.P.O. modding community

## Support our recreation and future mods

If you enjoyed our recreation of the original mod by [IvitoDev](https://thunderstore.io/c/repo/p/IvitoDev/SprayMod) and want to support me and my team for future updates and other mods, you can donate to us through PayPal.
https://www.paypal.me/MishaOpstal

## Support the original creator

If you enjoy the mod and want to support the original creator, you can donate them via PayPal:
https://www.paypal.me/IvitoDev

## Feedback & Support

For bugs, feature requests, or general feedback:
- Create an issue on [GitHub](https://github.com/OnTheLink/BiggerSprayMod/issues)

Enjoy spraying!
