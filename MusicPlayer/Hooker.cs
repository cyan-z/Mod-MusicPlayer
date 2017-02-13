﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Observables;
using ModuleAPI;

namespace MusicPlayer {

    [ApplicationHook]
    public class Hooker: Module {

        #region Useless Stuff
        public override string Name { get; } = "Music Player";
        public override string SemVer { get; } = "0.1.0";
        public override string Author => "Aryan Mann";
        public override Uri Website { get; } = new Uri("http://www.aryanmann.com/");

        public override void ConfigureSettings() { }
        public override void OnInitialized() {
            return;
        }
        public override void OnShutdown() {
            return;
        }
        #endregion

        //Instance of the SongList window
        SongList _slInstance;
        private CoreAudioDevice audioDevice;

        public Hooker() {
            audioDevice = new CoreAudioController().DefaultPlaybackDevice;
        }
        
        //Setup commands for Project Butler
        public override Dictionary<string, Regex> RegisteredCommands => new Dictionary<string, Regex>() {
            ["specific"] = new Regex("^(play|play song|song) (?<song>.+)$"),
            ["random"] = new Regex("^play ?(anything|something|random|any|whatever|music)$"),
            ["list all"] = new Regex("^(all|list) ?songs?$"),
            ["volume"] = new Regex("^vol (?<action>up|down|mute|zero|full|min|max)$"),
            ["set volume"] = new Regex(@"^vol ?set (?<volume>\d{1,3})$")
        };

        //These extensions will be recognized as valid music files
        public static string[] ValidExtensions = {
            ".mp3", ".m4a", ".ogg", ".wav", ".flv", ".wmv", ".ink", ".Ink", ".flac"
        };

        //Project Butler command hook
        public override void OnCommandRecieved(string commandName, string userInput) {
            if(commandName == "random") {
                PlayRandom();
            } else if(commandName == "specific") {
                string song = RegisteredCommands[commandName].Match(userInput).Groups["song"].Value.ToString();
                if(string.IsNullOrWhiteSpace(song)) { return; }

                PlayThis(song);
            } else if(commandName == "list all") {
                DisplaySongList();
            } else if(commandName == "volume") {
                HandleVolume(RegisteredCommands[commandName].Match(userInput).Groups["action"].Value);
            } else if (commandName == "set volume") {
                SetVolume(RegisteredCommands[commandName].Match(userInput).Groups["volume"].Value);
            }
        }

        public void HandleVolume(string whatDo) {
            if (audioDevice.State != DeviceState.Active || audioDevice == null) { return; }

            try {
                switch (whatDo) {
                    case "up": audioDevice.Volume += 10;
                        break;
                    case "down": audioDevice.Volume -= 10;
                        break;
                    case "mute": audioDevice.ToggleMute();
                        break;
                    case "zero":
                    case "min": audioDevice.Volume = 0;
                        break;
                    case "full":
                    case "max": audioDevice.Volume = 100;
                        break;
                }
            } catch { /* Bad Bad Practice :p */ }
        }

        public void SetVolume(string vol) {
            if(audioDevice.State != DeviceState.Active || audioDevice == null) { return; }

            int iVol;
            if(!int.TryParse(vol, out iVol)) { return; }

            if(iVol < 0) { iVol = 0; }
            if(iVol > 100) { iVol = 100;}

            audioDevice.Volume = iVol;
        }

        //Creates a popup that shows all songs ["list all"]
        public void DisplaySongList() {
            string songPath = Path.Combine(BaseDirectory, "Songs");
            if(!Directory.Exists(songPath)) { Directory.CreateDirectory(songPath); return; }

            List<string> files = Directory.GetFiles(songPath, "*", SearchOption.AllDirectories).Where(path => ValidExtensions.Contains(Path.GetExtension(path).ToLower()) || (IsShortcut(path) && ValidExtensions.Contains(Path.GetExtension(ResolveShortcut(path))))).ToList();

            if(_slInstance == null || !_slInstance.IsLoaded) {
                _slInstance = new SongList(files, BaseDirectory, "all");
            } else {
                _slInstance.FillPaths(files, "all");
                _slInstance.FillList();
            }
            _slInstance.Show();
        }

        //Play the first song that matches the user input ["specific"]
        public void PlayThis(string songName, bool showList = true) {
            string songPath = Path.Combine(BaseDirectory, "Songs");
            if(!Directory.Exists(songPath)) { Directory.CreateDirectory(songPath); return; }

            List<string> files = Directory.GetFiles(songPath, "*", SearchOption.AllDirectories).Where(path => ValidExtensions.Contains(Path.GetExtension(path).ToLower()) || (IsShortcut(path) && ValidExtensions.Contains(Path.GetExtension(ResolveShortcut(path))))).ToList();

            List<string> matchingFiles = new List<string>();
            files.ForEach(fl => {
                if(Regex.Match(Path.GetFileNameWithoutExtension(fl), songName, RegexOptions.IgnoreCase).Success) {
                    matchingFiles.Add(fl);
                }
            });

            if(matchingFiles.Count == 0) { return; } else if(matchingFiles.Count == 1) {
                Process pa = new Process() {
                    StartInfo = new ProcessStartInfo() {
                        FileName = matchingFiles[0],
                        WindowStyle = ProcessWindowStyle.Minimized
                    }
                };
                pa.Start();
            } else {
                if(showList) {
                    if(_slInstance == null || !_slInstance.IsLoaded) {
                        _slInstance = new SongList(matchingFiles, BaseDirectory, songName);
                    } else {
                        _slInstance.FillPaths(matchingFiles, songName);
                        _slInstance.FillList();
                    }
                    _slInstance.Show();
                } else {
                    Process pa = new Process() {
                        StartInfo = new ProcessStartInfo() {
                            FileName = matchingFiles[0],
                            WindowStyle = ProcessWindowStyle.Minimized
                        }
                    };
                    pa.Start();
                }
            }
        }

        private Random _r = new Random();
        //Play any random song from the base directory ["random"]
        public void PlayRandom() {
            string songPath = Path.Combine(BaseDirectory, "Songs");
            if(!Directory.Exists(songPath)) { Directory.CreateDirectory(songPath); return; }

            List<string> files = Directory.GetFiles(songPath, "*", SearchOption.AllDirectories).Where(path => ValidExtensions.Contains(Path.GetExtension(path).ToLower()) || (IsShortcut(path) && ValidExtensions.Contains(Path.GetExtension(ResolveShortcut(path))))).ToList();

            if(files.Count == 0) { return; }

            PlayThis(Path.GetFileNameWithoutExtension(files[_r.Next(0, files.Count)]), false);
        }

        //Checks to see if a file is a shortcut (.Ink extension)
        public static bool IsShortcut(string path) {
            string directory = Path.GetDirectoryName(path);
            string file = Path.GetFileName(path);

            Shell32.Shell shell = new Shell32.Shell();
            Shell32.Folder folder = shell.NameSpace(directory);
            Shell32.FolderItem folderItem = folder.ParseName(file);

            if(folderItem != null) { return folderItem.IsLink; }
            return false;
        }
        //Get the real path of a shortcut file
        public static string ResolveShortcut(string path) {
            string directory = Path.GetDirectoryName(path);
            string file = Path.GetFileName(path);

            Shell32.Shell shell = new Shell32.Shell();
            Shell32.Folder folder = shell.NameSpace(directory);
            Shell32.FolderItem folderItem = folder.ParseName(file);

            Shell32.ShellLinkObject link = (Shell32.ShellLinkObject) folderItem.GetLink;

            return link.Path;
        }

    }

}
