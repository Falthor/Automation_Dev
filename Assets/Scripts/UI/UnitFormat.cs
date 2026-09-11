using System.Globalization;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// How a power figure is written on screen: French decimal comma, and the decimal only when
    /// there is one.
    ///
    /// <b>Not rounded to a whole number</b>, which is what both power panels did until component
    /// draws stopped being whole: a memory module draws 2,5 kW, and Mathf.RoundToInt rounds a half
    /// to the even neighbour - so the Datacenter panel said "consomme 2 kW" for a building drawing
    /// 2,5. One decimal is enough for every figure the game has; a whole figure keeps no decimal,
    /// so "12 kW" does not become "12,0 kW".
    ///
    /// Built by hand rather than through a culture, which would depend on the machine's locale.
    /// </summary>
    public static class UnitFormat
    {
        public static string Kilowatts(float value)
        {
            float rounded = Mathf.Round(value * 10f) / 10f;
            string format = Mathf.Approximately(rounded, Mathf.Round(rounded)) ? "0" : "0.0";
            return rounded.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');
        }
    }
}
