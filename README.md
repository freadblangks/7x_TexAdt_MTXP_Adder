# 7x_TexAdt_MTXP_Adder
Updated version of Varen's [7x_TexAdt_MTXP_Adder](https://github.com/Varen/7x_TexAdt_MTXP_Adder) that in addition to texture scaling and height data also adds the height texture FileDataID block to ADTs.

Notable difference from the original is that ground effects remain unchanged/unedited and as such this only changes tex0 ADTs.

## Usage
- Download the latest release from the [releases](https://github.com/Marlamin/7x_TexAdt_MTXP_Adder/releases) page and extract it somewhere.
- Download the latest community listfile from the [listfile releases](https://github.com/wowdev/wow-listfile/releases), rename it to `listfile.csv` and put it in the extracted folder.
- Download the latest `TextureInfoByFilePath.json` from [Luzifix's ADTHeightDump repo](https://github.com/Luzifix/ADTHeightDump/tree/main/Output), rename it to `global.cfg` and put it in a new folder inside the extracted folder called `config`.
- Put ADTs that have already been partially upconverted (have an MDID chunk) in a new `Input` folder.
- Run the tool and it should output ADTs with the added MTXP/MHID chunks to the `Output` folder.

## Credits
All credits go to Varen for this tool and the initial height texturing implementation in Noggit Red as well as implave for suggesting the above improvements.