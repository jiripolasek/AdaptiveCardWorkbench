using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AdaptiveCardWorkbench.Services;

public sealed unsafe class SystemFontService
{
    private const byte DefaultCharset = 1;
    private const byte SymbolCharset = 2;
    private const byte VariablePitchFlag = 0x01;

    public IReadOnlyList<string> GetMonospaceFontFamilies()
    {
        HashSet<string> fontFamilies = new(
            StringComparer.CurrentCultureIgnoreCase);
        nint deviceContext = GetDC(nint.Zero);
        if (deviceContext == nint.Zero)
        {
            return [];
        }

        GCHandle fontFamiliesHandle = GCHandle.Alloc(fontFamilies);
        try
        {
            LogFont filter = default;
            filter.CharacterSet = DefaultCharset;
            _ = EnumFontFamiliesEx(
                deviceContext,
                &filter,
                &EnumerateFont,
                GCHandle.ToIntPtr(fontFamiliesHandle),
                0);
        }
        finally
        {
            fontFamiliesHandle.Free();
            _ = ReleaseDC(nint.Zero, deviceContext);
        }

        return fontFamilies
            .OrderBy(name => name, StringComparer.Create(
                CultureInfo.CurrentCulture,
                ignoreCase: true))
            .ToArray();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EnumerateFont(
        nint fontPointer,
        nint textMetricPointer,
        uint fontType,
        nint parameter)
    {
        try
        {
            EnumLogFontEx* font = (EnumLogFontEx*)fontPointer;
            TextMetric* textMetric = (TextMetric*)textMetricPointer;
            char* faceName = font->LogFont.FaceName;
            int faceNameLength = 0;
            while (faceNameLength < 32 && faceName[faceNameLength] != '\0')
            {
                faceNameLength++;
            }

            string familyName = new string(
                faceName,
                startIndex: 0,
                length: faceNameLength).Trim();
            bool isMonospace =
                (textMetric->PitchAndFamily & VariablePitchFlag) == 0;

            if (isMonospace &&
                textMetric->CharacterSet != SymbolCharset &&
                !string.IsNullOrWhiteSpace(familyName) &&
                !familyName.StartsWith('@') &&
                GCHandle.FromIntPtr(parameter).Target is
                    HashSet<string> fontFamilies)
            {
                fontFamilies.Add(familyName);
            }

            return 1;
        }
        catch
        {
            // Exceptions cannot cross an unmanaged callback boundary.
            return 0;
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint windowHandle, nint deviceContext);

    [DllImport(
        "gdi32.dll",
        EntryPoint = "EnumFontFamiliesExW",
        ExactSpelling = true)]
    private static extern int EnumFontFamiliesEx(
        nint deviceContext,
        LogFont* logFont,
        delegate* unmanaged[Stdcall]<nint, nint, uint, nint, int> callback,
        nint parameter,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct LogFont
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte CharacterSet;
        public byte OutputPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;
        public fixed char FaceName[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumLogFontEx
    {
        public LogFont LogFont;
        public fixed char FullName[64];
        public fixed char Style[32];
        public fixed char Script[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextMetric
    {
        public int Height;
        public int Ascent;
        public int Descent;
        public int InternalLeading;
        public int ExternalLeading;
        public int AverageCharacterWidth;
        public int MaximumCharacterWidth;
        public int Weight;
        public int Overhang;
        public int DigitizedAspectX;
        public int DigitizedAspectY;
        public char FirstCharacter;
        public char LastCharacter;
        public char DefaultCharacter;
        public char BreakCharacter;
        public byte Italic;
        public byte Underlined;
        public byte StruckOut;
        public byte PitchAndFamily;
        public byte CharacterSet;
    }
}
