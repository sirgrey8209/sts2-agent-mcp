# Third-party notices

## STS2MCP

https://github.com/Gennadiyev/STS2MCP

This project is a separate implementation, but some small fragments were adapted from STS2MCP:

- Rich-text tag stripping, including the `[img]...[/img]` → `[name]` rule (`src/Game/Text.cs`)
- Button label lookup through the `_label` / `_textLabel` / `_title` / `_buttonLabel` fields (`src/Game/Ui.cs`)
- Sorting card holders by on-screen position (`src/Game/Ui.cs`)
- The calls used to pick a card reward, claim a reward and choose a map node (`src/Screens/RewardHandlers.cs`, `src/Screens/MapHandler.cs`)

Its game-specific knowledge (shop, fake merchant and treasure handling, the v0.107 play-phase check, timeline caveats)
also informed the design.

License of STS2MCP:

```
Copyright 2026 Yikun Ji (Kunologist)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```
