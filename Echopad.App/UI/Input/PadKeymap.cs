using System.Collections.Generic;
using System.Windows.Input;

namespace Echopad.App.UI.Input
{
    /// <summary>
    /// App-layer keyboard → pad number mapping (1..16).
    /// Stays OUT of Core because Key is WPF-specific.
    ///
    /// Default layout (Option B), pad numbers 1..16:
    ///   Q W E R  ->  1  2  3  4
    ///   A S D F  ->  5  6  7  8
    ///   Z X C V  ->  9 10 11 12
    ///   T G B N  -> 13 14 15 16
    /// </summary>
    public sealed class PadKeymap
    {
        private readonly Dictionary<Key, int> _map;

        public PadKeymap()
        {
            _map = CreateDefaultOptionB_OneBased();
        }

        /// <summary>
        /// Returns true if the key maps to a pad number (1..16).
        /// </summary>
        public bool TryGetPadNumber(Key key, out int padNumber)
            => _map.TryGetValue(key, out padNumber);

        // ----------------------------
        // Defaults
        // ----------------------------
        private static Dictionary<Key, int> CreateDefaultOptionB_OneBased()
        {
            return new Dictionary<Key, int>
            {
                // Row 1
                { Key.Q, 1 }, { Key.W, 2 }, { Key.E, 3 }, { Key.R, 4 },

                // Row 2
                { Key.A, 5 }, { Key.S, 6 }, { Key.D, 7 }, { Key.F, 8 },

                // Row 3
                { Key.Z, 9 }, { Key.X, 10 }, { Key.C, 11 }, { Key.V, 12 },

                // Row 4
                { Key.T, 13 }, { Key.G, 14 }, { Key.B, 15 }, { Key.N, 16 },
            };
        }

        // FUTURE hooks (intentionally not implemented yet)
        // public static PadKeymap LoadFromFile(string path) { ... }
        // public void SaveToFile(string path) { ... }
    }
}
