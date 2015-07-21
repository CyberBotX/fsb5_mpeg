# fsb5_mpeg

This is a tool to remove the MPEG padding that FMOD added to MP3-based FSB5s. The tool was inspired in part by a similar tool by hcs that was designed for FSB3 and FSB4.

If you have an FSB3 or FSB4 that needs its MPEG data un-padded, you can get *fsb_mpeg* from [hcs64.com](http://hcs64.com/vgm_ripping.html).

## Compiling

You will need to compile the tool with Visual Studio, at least version 2010 (the earliest with .NET 4 support as well as the earlier that supports the code's Solution/Project files).

## Running

The tool is a C# console tool. You will need to run it from the command line.
Syntax is:

```
fsb5_mpeg.exe <fsb file> [<output file>]
```

If the output file is not given, the tool will attempt to overwrite the given FSB5 file.

## Caveats

* Currently the tool will only handle single-stream MP3-based FSB5 files.
  * To remedy this, use my [fsb5_split](http://github.com/CyberBotX/fsb5_split).
* There is no warning given if you exclude an output file.
* There is little error checking in place, so if the tool fails on you, please file an issue on GitHub.

## Contact

If you have any questions, comments, or concerns, you may email me at cyberbotx@cyberbotx.com. Additionally, if you spot any issues, please file an issue on GitHub.