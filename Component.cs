﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.ASL;
using LiveSplit.Model;
using LiveSplit.Options;

namespace LiveSplit.UI.Components
{
    public class ASLComponent : LogicComponent
    {
        public override string ComponentName => "Scriptable Auto Splitter";

        // public so other components (ASLVarViewer) can access
        public ASLScript Script { get; private set; }

        private bool _do_reload;
        private string _old_script_path;

        private Timer _update_timer;
        private FileSystemWatcher _fs_watcher;

        private ComponentSettings _settings;

        private LiveSplitState _state;

        public ASLComponent(LiveSplitState state)
        {
            _state = state;

            _settings = new ComponentSettings();

            _fs_watcher = new FileSystemWatcher();
            _fs_watcher.Changed += async (sender, args) => {
                await Task.Delay(200);
                _do_reload = true;
            };

            // -try- to run a little faster than 60hz
            // note: Timer isn't very reliable and quite often takes ~30ms
            // we need to switch to threading
            _update_timer = new Timer() { Interval = 15 };
            _update_timer.Tick += (sender, args) => UpdateScript();
            _update_timer.Enabled = true;
        }

        public ASLComponent(LiveSplitState state, string script_path)
            : this(state)
        {
            _settings = new ComponentSettings() { ScriptPath = script_path };
        }

        public override void Dispose()
        {
            ScriptCleanup();

            _fs_watcher?.Dispose();
            _update_timer?.Dispose();
        }

        public override Control GetSettingsControl(LayoutMode mode)
        {
            return _settings;
        }

        public override XmlNode GetSettings(XmlDocument document)
        {
            return _settings.GetSettings(document);
        }

        public override void SetSettings(XmlNode settings)
        {
            _settings.SetSettings(settings);
        }

        public override void Update(IInvalidator invalidator, LiveSplitState state, float width, float height,
            LayoutMode mode) { }


        private void UpdateScript()
        {
            // Disable timer, to wait for execution of this iteration to
            // finish. This can be useful if blocking operations like
            // showing a message window are used.
            _update_timer.Enabled = false;

            // this is ugly, fix eventually!
            if (_settings.ScriptPath != _old_script_path || _do_reload)
            {
                try
                {
                    _do_reload = false;
                    _old_script_path = _settings.ScriptPath;

                    ScriptCleanup();

                    if (string.IsNullOrEmpty(_settings.ScriptPath))
                    {
                        // Only disable file watcher if script path changed to empty
                        // (otherwise detecting file changes may still be wanted)
                        _fs_watcher.EnableRaisingEvents = false;
                    }
                    else
                    {
                        LoadScript();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }

            if (Script != null)
            {
                try
                {
                    Script.Update(_state);
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }

            _update_timer.Enabled = true;
        }

        private void LoadScript()
        {
            Log.Info("[ASL] Loading new script: " + _settings.ScriptPath);

            _fs_watcher.Path = Path.GetDirectoryName(_settings.ScriptPath);
            _fs_watcher.Filter = Path.GetFileName(_settings.ScriptPath);
            _fs_watcher.EnableRaisingEvents = true;

            // New script
            Script = ASLParser.Parse(File.ReadAllText(_settings.ScriptPath));

            Script.RefreshRateChanged += (sender, rate) => _update_timer.Interval = (int)Math.Round(1000 / rate);
            _update_timer.Interval = (int)Math.Round(1000 / Script.RefreshRate);

            Script.GameVersionChanged += (sender, version) => _settings.SetGameVersion(version);
            _settings.SetGameVersion(null);

            // Give custom ASL settings to GUI, which populates the list and
            // stores the ASLSetting objects which are shared between the GUI
            // and ASLScript
            try
            {
                ASLSettings settings = Script.RunStartup(_state);
                _settings.SetASLSettings(settings, true);
            }
            catch (Exception ex)
            {
                // Script already created, but startup failed, so clean up again
                Log.Error(ex);
                ScriptCleanup();
            }
        }

        private void ScriptCleanup()
        {
            if (Script == null)
                return;

            try
            {
                Script.RunShutdown(_state);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            finally
            {
                Script.Dispose();
                _settings.SetGameVersion(null);
                _settings.SetASLSettings(new ASLSettings(), false);

                // Script should no longer be used, even in case of error
                // (which the ASL shutdown method may contain)
                Script = null;
            }
        }
    }
}
