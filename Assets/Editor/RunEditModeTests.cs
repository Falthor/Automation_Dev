using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Runs the EditMode suite and writes a plain-text report to <c>Temp/test-report.txt</c>.
    ///
    /// <b>Why this exists rather than just pressing the button.</b> The Test Runner window is the
    /// normal way in, and this changes nothing about it. What this adds is a way to start a run from
    /// <i>outside</i> the editor: dropping a file at <c>Temp/run-tests</c> starts the suite within a
    /// second, needing nothing else. The editor automation bridge refuses
    /// <see cref="TestRunnerApi.Execute"/> as an interactive call, so a run cannot be started
    /// through it - and a tool that cannot run the tests cannot check its own work.
    ///
    /// The report is a file rather than console output because a run spans a domain reload: whoever
    /// asked for it is not around to read a log by the time it finishes.
    /// </summary>
    public static class RunEditModeTests
    {
        /// <summary>Its presence asks for a run, and it is deleted before the run starts - so a crashed run does not ask again forever.</summary>
        const string SentinelPath = "Temp/run-tests";

        const string ReportPath = "Temp/test-report.txt";

        [MenuItem("Tools/Tests/Run EditMode suite")]
        public static void Run()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var sink = ScriptableObject.CreateInstance<Report>();

            // Both must survive the run without being saved into anything. Left collectable, the
            // sink is gone before RunFinished and the run reports nothing at all.
            api.hideFlags = HideFlags.HideAndDontSave;
            sink.hideFlags = HideFlags.HideAndDontSave;

            api.RegisterCallbacks(sink);
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        /// <summary>How often the sentinel is looked for. Once a second is far below human patience and far above a per-frame cost.</summary>
        const double PollIntervalSeconds = 1.0;

        static double _nextPollAt;

        /// <summary>
        /// Watches for the sentinel on the editor's own tick.
        ///
        /// <b>It used to run only on load, and that was a trap.</b> An <c>[InitializeOnLoadMethod]</c>
        /// fires on a script reload - so dropping the sentinel started a run only when a C# file had
        /// also changed. Drop it after asset-only work and nothing recompiles, nothing reloads, the
        /// hook never fires, and the file sits there while whoever asked waits for a report that was
        /// never going to come. It looked like Unity being slow; it was a run that had not started.
        ///
        /// Polling makes dropping the file sufficient on its own, which is the whole contract.
        ///
        /// <b>One ordering rule comes with it.</b> The poll fires within a second, so dropping the
        /// sentinel immediately after editing a source file starts the suite against the assembly
        /// that is still on disk - the edit has not compiled yet. Edit, let the compile finish, then
        /// drop the file. <c>isCompiling</c> is checked here but cannot help: Unity has not noticed
        /// the change at that point, so there is nothing yet to be compiling.
        /// </summary>
        [InitializeOnLoadMethod]
        static void WatchForRequests()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPollAt) return;
            _nextPollAt = EditorApplication.timeSinceStartup + PollIntervalSeconds;

            if (EditorApplication.isCompiling || EditorApplication.isPlaying) return;
            if (!File.Exists(SentinelPath)) return;

            File.Delete(SentinelPath);
            Run();
        }

        /// <summary>Collects the run into the report file. One line for the totals, one per failure, and the measurements a test chose to write out.</summary>
        class Report : ScriptableObject, ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var text = new StringBuilder();
                text.AppendLine($"passed={result.PassCount} failed={result.FailCount} "
                    + $"skipped={result.SkipCount} inconclusive={result.InconclusiveCount}");

                Walk(result, text);

                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, text.ToString());
                Debug.Log($"EditMode suite: {result.PassCount} passed, {result.FailCount} failed. Report at {ReportPath}");
            }

            static void Walk(ITestResultAdaptor node, StringBuilder text)
            {
                if (node.HasChildren)
                {
                    foreach (ITestResultAdaptor child in node.Children) Walk(child, text);
                    return;
                }

                if (node.TestStatus == TestStatus.Failed)
                {
                    text.AppendLine($"FAIL {node.FullName}");
                    text.AppendLine($"     {(node.Message ?? string.Empty).Replace("\n", " | ")}");
                    return;
                }

                // What a passing test measured. The numbers are the point of a test that watches a
                // shape rather than a value, and they are invisible in a pass/fail count.
                if (!string.IsNullOrEmpty(node.Output))
                {
                    text.AppendLine($"MEAS {node.Test.Name}: {node.Output.Trim().Replace("\n", " | ")}");
                }
            }
        }
    }
}
