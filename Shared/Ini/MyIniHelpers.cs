using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    /// <summary>Pure-static helpers for the <see cref="MyIni"/> wrapper. Shorthand for the verbose default-with-fallback pattern.</summary>
    public static class MyIniHelpers
    {
        /// <summary>Returns the integer value at <paramref name="section"/>/<paramref name="key"/>, or <paramref name="fallback"/> when absent.</summary>
        public static int GetInt(MyIni ini, string section, string key, int fallback)
        {
            return ini.Get(section, key).ToInt32(fallback);
        }

        /// <summary>Returns the long value at <paramref name="section"/>/<paramref name="key"/>, or <paramref name="fallback"/> when absent.</summary>
        public static long GetLong(MyIni ini, string section, string key, long fallback)
        {
            return ini.Get(section, key).ToInt64(fallback);
        }

        /// <summary>Returns the double value at <paramref name="section"/>/<paramref name="key"/>, or <paramref name="fallback"/> when absent.</summary>
        public static double GetDouble(MyIni ini, string section, string key, double fallback)
        {
            return ini.Get(section, key).ToDouble(fallback);
        }

        /// <summary>Returns the float value at <paramref name="section"/>/<paramref name="key"/>, or <paramref name="fallback"/> when absent.</summary>
        public static float GetFloat(MyIni ini, string section, string key, float fallback)
        {
            return (float)ini.Get(section, key).ToDouble(fallback);
        }

        /// <summary>Returns the boolean value at <paramref name="section"/>/<paramref name="key"/>, or <paramref name="fallback"/> when absent.</summary>
        public static bool GetBool(MyIni ini, string section, string key, bool fallback)
        {
            return ini.Get(section, key).ToBoolean(fallback);
        }

        /// <summary>Returns the string value at <paramref name="section"/>/<paramref name="key"/>, or <paramref name="fallback"/> when absent.</summary>
        public static string GetString(MyIni ini, string section, string key, string fallback)
        {
            return ini.Get(section, key).ToString(fallback);
        }

        /// <summary>Fills <paramref name="output"/> with all keys in <paramref name="section"/>. Output list is cleared on entry.</summary>
        public static void GetKeys(MyIni ini, string section, List<MyIniKey> output)
        {
            output.Clear();
            ini.GetKeys(section, output);
        }
    }
}
