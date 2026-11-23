# BankPerfect V5 codec

The alphabet, substitution tables and decoding logic in `V5Codec.cs` are
derived from
[`codec_v5.rs` at revision 990f5c7f712dab07c7fefc2921cbf60beee7f6b1](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/codec_v5.rs).
The V5 file separator and the `bp_script_data_` prefix used by
`V5Parser.cs` are taken from
[`v5_text.rs` at the same revision](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/v5_text.rs).

No proprietary BankPerfect 8 source code, script or graphical asset is
included in Moneta. The rest of the V5 reader is the independent Moneta C#
implementation and validation pipeline.

MIT License

Copyright (c) 2026 Fabio Chelly

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
