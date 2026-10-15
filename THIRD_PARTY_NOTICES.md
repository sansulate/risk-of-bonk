# Third-party notices

## AssetsTools.NET 3.0.5 (lib/AssetsTools.NET.dll)
https://github.com/nesrak1/AssetsTools.NET, MIT License:

    MIT License
    
    Copyright (c) 2020 nesrak1
    
    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:
    
    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.
    
    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
## classdata.tpk (lib/classdata.tpk)
The Unity type database distributed with AssetsTools.NET / UABEA by nesrak1 (https://github.com/nesrak1/UABEA, MIT License, same terms as above). It describes Unity's built-in types and contains no game content.

## Newtonsoft.Json (lib/Newtonsoft.Json.dll, bundled in RiskOfBonk/)
https://github.com/JamesNK/Newtonsoft.Json, MIT License, Copyright (c) 2007 James Newton-King (same terms as above). Risk of Rain 2's own code refers to it but the game
does not ship it; the copy is the one RoR2BepInExPack 1.46.0 (Risk of Thunder, MIT) distributes.

## NVorbis 0.8.6 (lib/NVorbis.dll, bundled in RiskOfBonk/)
https://github.com/NVorbis/NVorbis, MIT License, Copyright (c) Andrew Ward (same terms as above). Decodes Megabonk's
sounds (read from the player's own Megabonk install) so the mod can play them.

## python-fsb5 (method only, no code or data shipped)
https://github.com/HearthSim/python-fsb5, MIT License, Copyright (c) 2016 Simon Pinfold. FsbVorbis.cs follows its way of
rebuilding FMOD FSB5 Vorbis samples as Ogg Vorbis; the Vorbis setup headers are read from the FMOD inside the player's own
Megabonk install, never shipped.

