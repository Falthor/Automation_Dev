using System.IO;
using UnityEngine;

namespace Game.Gameplay.Exploration
{
    /// <summary>
    /// <b>A development instrument, and it is meant to be deleted.</b> Not a feature: nothing reads
    /// what it writes, nothing depends on it existing, and it is documented only in the notebook.
    /// When the card threshold has been settled, delete this file, its field on
    /// <c>ExplorerRobotSettings</c>, and the four lines that feed it in
    /// <see cref="ExplorerRobotSystem"/>.
    ///
    /// <b>The question it answers.</b> A card per 2 500 newly discovered cells assumes a robot
    /// opening virgin ground at every step, which only happens on a clean frontier. In practice the
    /// pull towards the unknown leans the heading without obliging it, and the recall at 330 makes
    /// the robot follow a frontier it has already opened - so the real yield is unknown and the
    /// threshold is a guess. This measures it.
    ///
    /// One line a minute, beside the save (<c>docs/BUILD.md</c> §6). A minute is coarse on purpose:
    /// the figure wanted is a rate, and a per-frame log would cost more than it measures.
    /// </summary>
    public sealed class ExplorerHarvestLog
    {
        const string FileName = "explorer-harvest-log.csv";
        const float MinuteSeconds = 60f;

        readonly string _path;
        bool _broken;

        float _seconds;
        int _newCells;
        float _distanceCells;
        int _cards;

        /// <summary>
        /// The wrecks found in the current minute, and how far out each was.
        ///
        /// Two columns rather than one, because the question is a rhythm: a count says how often and
        /// the distances say where, and the minute number already in the first column says when. A
        /// StringBuilder rather than a list of floats - it is written once a minute and thrown away.
        /// </summary>
        int _wrecks;

        readonly System.Text.StringBuilder _wreckDistances = new System.Text.StringBuilder();

        /// <summary>Minutes written so far. Watched by nothing but a test - it is what says the thing actually wrote.</summary>
        public int MinutesWritten { get; private set; }

        public string Path => _path;

        public ExplorerHarvestLog()
        {
            _path = System.IO.Path.Combine(Application.persistentDataPath, FileName);
        }

        /// <summary>Test seam: writes wherever it is told, so a test never touches the player's own log.</summary>
        public ExplorerHarvestLog(string path)
        {
            _path = path;
        }

        public void RecordNewCells(int cells)
        {
            if (cells > 0) _newCells += cells;
        }

        public void RecordDistance(float cells)
        {
            if (cells > 0f) _distanceCells += cells;
        }

        public void RecordCard() => _cards++;

        /// <summary>One wreck found, with how far from the Core it was. Rare enough that the string never grows past a handful of numbers.</summary>
        public void RecordWreck(float distanceFromCoreCells)
        {
            _wrecks++;

            if (_wreckDistances.Length > 0) _wreckDistances.Append(' ');
            _wreckDistances.Append(distanceFromCoreCells.ToString("0.#"));
        }

        /// <summary>
        /// Advances the minute and writes a line when one has passed. Accumulating into four fields
        /// and touching the disk once a minute is what keeps this off the per-frame budget entirely.
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;

            _seconds += deltaSeconds;
            if (_seconds < MinuteSeconds) return;

            Write();

            _seconds -= MinuteSeconds;
            _newCells = 0;
            _distanceCells = 0f;
            _cards = 0;
            _wrecks = 0;
            _wreckDistances.Clear();
        }

        void Write()
        {
            if (_broken) return;

            try
            {
                if (!File.Exists(_path))
                {
                    // distance_cells is exploring travel only: the return leg reveals nothing by
                    // design, so counting it would make the yield figure depend on how often the
                    // player recalls rather than on how the wander behaves.
                    // wreck_distances_cells is space-separated inside its own column: a wreck is an
                    // event, not a rate, so the minute it was found in and how far out it was are
                    // both needed to judge whether the rings are tuned right.
                    File.AppendAllText(_path, "play_minutes,new_cells,distance_cells,cards,wrecks,wreck_distances_cells\n");
                }

                MinutesWritten++;
                File.AppendAllText(_path,
                    $"{MinutesWritten},{_newCells},{_distanceCells:0.#},{_cards},{_wrecks},{_wreckDistances}\n");
            }
            catch (IOException error)
            {
                // A measurement instrument must never be able to take the game down with it - a
                // locked file or a full disk stops the log and nothing else.
                _broken = true;
                Debug.LogWarning($"ExplorerHarvestLog: stopped logging to {_path} - {error.Message}");
            }
        }
    }
}
