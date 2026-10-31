using SFML.Graphics;
using System;
using System.Collections.Generic;
using System.CommandLine.Parsing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpineViewerCLI
{
    public static class Utils
    {
        public static FixedViewOptions? ParseFixedView(ArgumentResult result)
        {
            const string separator = ",";
            const string assignment = "=";

            var token = result.Tokens.Count > 0 ? result.Tokens[0].Value : null;
            if (string.IsNullOrWhiteSpace(token))
                return null;

            try
            {
                var pairs = token.Split(separator);
                var opts = new FixedViewOptions();
                bool ret;
                foreach (var it in pairs)
                {
                    var kv = it.Trim().Split(assignment);
                    if (kv.Length != 2)
                    {
                        result.AddError($"Invalid fixed view format: {it}.");
                        return null;
                    }
                    switch (kv[0].ToLowerInvariant().Trim())
                    {
                        case "w":
                            ret = uint.TryParse(kv[1], out var w); 
                            if (!ret || w <= 0)
                            {
                                result.AddError($"Invalid width value: {kv[1]}.");
                                return null;
                            }
                            opts.Width = w;
                            break;
                        case "h":
                            ret = uint.TryParse(kv[1], out var h);
                            if (!ret || h <= 0)
                            {
                                result.AddError($"Invalid height value: {kv[1]}.");
                                return null;
                            }
                            opts.Height = h;
                            break;
                        case "x":
                            ret = float.TryParse(kv[1], out var x);
                            if (!ret)
                            {
                                result.AddError($"Invalid center x value: {kv[1]}.");
                                return null;
                            }
                            opts.CenterX = x;
                            break;
                        case "y":
                            ret = float.TryParse(kv[1], out var y);
                            if (!ret)
                            {
                                result.AddError($"Invalid center y value: {kv[1]}.");
                                return null;
                            }
                            opts.CenterY = y;
                            break;
                        case "s":
                            ret = float.TryParse(kv[1], out var s);
                            if (!ret || s <= 0)
                            {
                                result.AddError($"Invalid scale value: {kv[1]}.");
                                return null;
                            }
                            opts.Scale = s;
                            break;
                        default:
                            result.AddError($"Invalid arg value: {it}.");
                            return null;
                    }
                }
                if (opts.Width <= 0 || opts.Height <= 0)
                {
                    result.AddError($"Width and height must be set.");
                    return null;
                }
                return opts;
            }
            catch
            {
                result.AddError("Invalid fixed view format.");
                return null;
            }
        }

        public static Color ParseColor(ArgumentResult result)
        {
            var token = result.Tokens.Count > 0 ? result.Tokens[0].Value : null;
            if (string.IsNullOrWhiteSpace(token))
                return Color.Black;

            try
            {
                // 去掉开头的 #
                var hex = token.Trim().TrimStart('#');

                // 支持格式: RGB / ARGB / RRGGBB / AARRGGBB
                if (hex.Length == 3)
                {
                    // #RGB → #RRGGBB
                    var r = hex[0];
                    var g = hex[1];
                    var b = hex[2];
                    hex = $"{r}{r}{g}{g}{b}{b}";
                    hex = "FF" + hex; // 加上不透明 alpha
                }
                else if (hex.Length == 4)
                {
                    // #ARGB → #AARRGGBB
                    var a = hex[0];
                    var r = hex[1];
                    var g = hex[2];
                    var b = hex[3];
                    hex = $"{a}{a}{r}{r}{g}{g}{b}{b}";
                }
                else if (hex.Length == 6)
                {
                    // #RRGGBB → #AARRGGBB
                    hex = "FF" + hex;
                }
                else if (hex.Length != 8)
                {
                    result.AddError("Invalid color format. Use #RGB, #ARGB, #RRGGBB, or #AARRGGBB.");
                    return Color.Black;
                }

                var aVal = Convert.ToByte(hex[..2], 16);
                var rVal = Convert.ToByte(hex.Substring(2, 2), 16);
                var gVal = Convert.ToByte(hex.Substring(4, 2), 16);
                var bVal = Convert.ToByte(hex.Substring(6, 2), 16);
                return new(rVal, gVal, bVal, aVal);
            }
            catch
            {
                result.AddError("Invalid color format.");
                return Color.Black;
            }
        }
    }
}
