﻿using Newtonsoft.Json;
using SharpCompress.Archive;
using SharpCompress.Common;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace ModAnalyzer.Domain {
    internal class PluginAnalyzer {
        private readonly BackgroundWorker _backgroundWorker;
        private readonly Game _game;

        public PluginAnalyzer(BackgroundWorker backgroundWorker) {
            _backgroundWorker = backgroundWorker;
            // TODO: remove hardcoding (requires making a gui component from which the user can choose the game mode)
            _game = GameService.GetGame("Skyrim");
        }

        public PluginDump GetPluginDump(IArchiveEntry entry) {
            try {
                _backgroundWorker.ReportMessage(Environment.NewLine + "Getting plugin dump for " + entry.Key + "...", true);
                ExtractPlugin(entry);
                return AnalyzePlugin(entry);
            }
            catch (Exception exception) {
                _backgroundWorker.ReportMessage("Failed to analyze plugin.", false);
                _backgroundWorker.ReportMessage("Exception: " + exception.Message, false);

                return null;
            }
            finally {
                _backgroundWorker.ReportMessage(" ", false);
                RevertPlugin(entry);
            }
        }

        public void ExtractPlugin(IArchiveEntry entry) {
            string gameDataPath = GameService.GetGamePath(_game);
            string pluginFileName = Path.GetFileName(entry.Key);
            string pluginFilePath = Path.Combine(gameDataPath, pluginFileName);

            _backgroundWorker.ReportMessage("Extracting " + entry.Key + "...", true);

            if (File.Exists(pluginFilePath) && !File.Exists(pluginFilePath + ".bak"))
                File.Move(pluginFilePath, pluginFilePath + ".bak");

            entry.WriteToDirectory(gameDataPath, ExtractOptions.Overwrite);
        }

        private void GetModDumpMessages(StringBuilder message) {
            ModDump.GetBuffer(message, message.Capacity);
            if (message.Length > 0) {
                string messageString = message.ToString();
                if (messageString.EndsWith("\n") && !messageString.EndsWith(" \n")) {
                    messageString = messageString.TrimEnd();
                }
                _backgroundWorker.ReportMessage(messageString, false);
                ModDump.FlushBuffer();
            }
        }

        // TODO: refactor
        public PluginDump AnalyzePlugin(IArchiveEntry entry) {
            // start mod dump
            ModDump.LoadModDump();
            ModDump.StartModDump();
            ModDump.SetGameMode(_game.gameMode);

            // prepare variables for analysis
            _backgroundWorker.ReportMessage("Analyzing " + entry.Key + "...\n", true);
            StringBuilder message = new StringBuilder(4 * 1024 * 1024);

            // prepare plugin file for dumping
            if (!ModDump.Prepare(Path.GetFileName(entry.Key))) {
                GetModDumpMessages(message);
                return null;
            }

            // dump the plugin file
            StringBuilder json = new StringBuilder(4 * 1024 * 1024); // 4MB maximum dump size
            if (!ModDump.Dump()) {
                GetModDumpMessages(message);
                return null;
            }

            // use a loop to poll for messages until the dump is ready
            while (!ModDump.GetDumpResult(json, json.Capacity)) {
                GetModDumpMessages(message);
                // wait 100ms between each polling operation so we don't bring things to a standstill with this while loop
                // we can do this without locking up the UI because this is happening in a background worker
                System.Threading.Thread.Sleep(100);
            }
            
            // get any remaining messages
            GetModDumpMessages(message);

            // stop mod dump
            ModDump.FlushBuffer();
            ModDump.UnloadModDump();

            // deserialize and return plugin dump
            return JsonConvert.DeserializeObject<PluginDump>(json.ToString());
        }

        public void RevertPlugin(IArchiveEntry entry) {
            try {
                string dataPath = GameService.GetGamePath(_game);
                string fileName = Path.GetFileName(entry.Key);
                string filePath = dataPath + fileName;

                File.Delete(filePath);

                if (File.Exists(filePath + ".bak"))
                    File.Move(filePath + ".bak", filePath);
            } catch (Exception e) {
                _backgroundWorker.ReportMessage("Failed to revert plugin!", false);
                _backgroundWorker.ReportMessage("!!! Please manually revert " + Path.GetFileName(entry.Key) + "!!!", false);
                _backgroundWorker.ReportMessage("Exception:" + e.Message, false);
            }
        }
    }
}