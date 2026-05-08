<p align="center">
  <img src="https://raw.githubusercontent.com/briansokol/SE-Goose/main/Goose/thumb.png" alt="Goose thumbnail" width="540" />
  <br />
  <sub>Thumbnail by <a href="https://www.youtube.com/@RevPlays898">RevPlaysGames</a></sub>
</p>

# Goose

An automatic inventory sorter for Space Engineers. A single Programmable Block script that watches your grid's containers and quietly keeps everything in the right place.

You tag containers with the categories of items they should hold, and Goose handles the rest, moving items into place, topping up stock containers, and respecting per-container priorities and exclusions.

This script was created with the aid of AI (Claude Code). No, it was not vibe coded.

## Documentation

Full design and usage documentation lives in the project wiki:

- **[SE-Goose Wiki](https://github.com/briansokol/SE-Goose/wiki)** - Start Here
- [Technical Architecture](https://github.com/briansokol/SE-Goose/wiki/Technical-Architecture-Design)

## Installing the script from this repo

All releases are published on GitHub:

**[SE-Goose Releases](https://github.com/briansokol/SE-Goose/releases)**

To install a release into your game:

1. Open the [Releases page](https://github.com/briansokol/SE-Goose/releases) and pick the version you want (the latest is at the top).
2. Expand the **Assets** section under that release.
3. Download `script.txt`.
4. Open `script.txt` in any text editor and copy the entire contents to your clipboard.
5. In Space Engineers, open the Programmable Block you want to run Goose on, click **Edit**, clear the existing code, and paste the contents of `script.txt`.
6. Click OK, save and exit. Goose will start running on the next tick.

To update to a newer release later, repeat the same steps with the new `script.txt`.

## License

See [LICENSE](LICENSE).
