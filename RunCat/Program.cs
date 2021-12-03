// Copyright 2020 Takuto Nakamura
// 
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
// 
//        http://www.apache.org/licenses/LICENSE-2.0
// 
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.

using RunCat.Properties;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Resources;
using System.ComponentModel;

namespace RunCat
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // terminate runcat if there's any existing instance
            var procMutex = new System.Threading.Mutex(true, "_RUNCAT_MUTEX", out var result);
            if (!result)
            {
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new RunCatApplicationContext());

            procMutex.ReleaseMutex();
        }
    }

    public class RunCatApplicationContext : ApplicationContext
    {
        private const int CPU_TIMER_DEFAULT_INTERVAL = 3000;
        private const int ANIMATE_TIMER_DEFAULT_INTERVAL = 200;
        private PerformanceCounter cpuUsage;
        private PerformanceCounter memoryUsage;
        private ToolStripMenuItem runnerMenu;
        private ToolStripMenuItem rangeMenu;
        private ToolStripMenuItem themeMenu;
        private ToolStripMenuItem startupMenu;
        private NotifyIcon notifyIcon;
        private string runner = "";
        private int current = 0;
        private string systemTheme = "";
        private string manualTheme = "";
        private string monitor = "";
        private Icon[] icons;
        private Icon[] iconsD = new Icon[]{};
        private Icon[] iconsNomel;
        private Timer animateTimer = new Timer();
        private Timer cpuTimer = new Timer();

        private float total;
        private int monitorCount = 0;
        private Dictionary<string, int> map = new Dictionary<string, int>
        {
            {"cat", 5}, {"parrot", 10}, {"other", 36}, {"fufu", 8}, {"fufud", 12},{"leishen", 54}, {"shenlilinhua", 48}
        };
        

        public RunCatApplicationContext()
        {
            UserSettings.Default.Reload();
            runner = UserSettings.Default.Runner;
            manualTheme = UserSettings.Default.Theme;
            monitor = UserSettings.Default.Monitor;

            Application.ApplicationExit += new EventHandler(OnApplicationExit);

            SystemEvents.UserPreferenceChanged += new UserPreferenceChangedEventHandler(UserPreferenceChanged);

            cpuUsage = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            memoryUsage = new PerformanceCounter("Memory","Available MBytes");
            total = new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory;
            Console.WriteLine(total);
            
            _ = cpuUsage.NextValue(); // discards first return value
            _ = memoryUsage.NextValue(); // discards first return value

            runnerMenu = new ToolStripMenuItem("Target", null, new ToolStripMenuItem[]
            {
                new ToolStripMenuItem("Cat", null, SetRunner)
                {
                    Checked = runner.Equals("cat")
                },
                new ToolStripMenuItem("Parrot", null, SetRunner)
                {
                    Checked = runner.Equals("parrot")
                },
                new ToolStripMenuItem("Other", null, SetRunner)
                {
                    Checked = runner.Equals("other")
                },
                new ToolStripMenuItem("FuFu", null, SetRunner)
                {
                    Checked = runner.Equals("fufu")
                },
                new ToolStripMenuItem("LeiShen", null, SetRunner)
                {
                    Checked = runner.Equals("leishen")
                },
                new ToolStripMenuItem("ShenLiLinHua", null, SetRunner)
                {
                Checked = runner.Equals("shenlilinhua")
                }
            });
            
            
            rangeMenu = new ToolStripMenuItem("Runner", null, new ToolStripMenuItem[]
            {
                new ToolStripMenuItem("CPU", null, SetMonitor)
                {
                    Checked = monitor.Equals("cpu")
                },
                new ToolStripMenuItem("Memory", null, SetMonitor)
                {
                    Checked = monitor.Equals("memory")
                },
            });

            themeMenu = new ToolStripMenuItem("Theme", null, new ToolStripMenuItem[]
            {
                new ToolStripMenuItem("Default", null, SetThemeIcons)
                {
                    Checked = manualTheme.Equals("")
                },
                new ToolStripMenuItem("Light", null, SetLightIcons)
                {
                    Checked = manualTheme.Equals("light")
                },
                new ToolStripMenuItem("Dark", null, SetDarkIcons)
                {
                    Checked = manualTheme.Equals("dark")
                }
            });

            startupMenu = new ToolStripMenuItem("Startup", null, SetStartup);
            if (IsStartupEnabled())
            {
                startupMenu.Checked = true;
            }

            ContextMenuStrip contextMenuStrip = new ContextMenuStrip(new Container());
            contextMenuStrip.Items.AddRange(new ToolStripItem[]
            {
                runnerMenu,
                rangeMenu,
                themeMenu,
                startupMenu,
                new ToolStripMenuItem("Exit", null, Exit)
            });

            notifyIcon = new NotifyIcon()
            {
                Icon = Resources.light_cat_0,
                ContextMenuStrip = contextMenuStrip,
                Text = "0.0%",
                Visible = true
            };

            notifyIcon.DoubleClick += new EventHandler(HandleDoubleClick);

            UpdateThemeIcons();
            SetAnimation();
            CPUTick();
            StartObserveCPU();
            current = 1;
        }
        private void OnApplicationExit(object sender, EventArgs e)
        {
            UserSettings.Default.Runner = runner;
            UserSettings.Default.Theme = manualTheme;
            UserSettings.Default.Monitor = monitor;
            UserSettings.Default.Save();
        }

        private bool IsStartupEnabled()
        {
            string keyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (RegistryKey rKey = Registry.CurrentUser.OpenSubKey(keyName))
            {
                return (rKey.GetValue(Application.ProductName) != null) ? true : false;
            }
        }

        private string GetAppsUseTheme()
        {
            string keyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            using (RegistryKey rKey = Registry.CurrentUser.OpenSubKey(keyName))
            {
                object value;
                if (rKey == null || (value = rKey.GetValue("SystemUsesLightTheme")) == null)
                {
                    Console.WriteLine("Oh No! Couldn't get theme light/dark");
                    return "light";
                }
                int theme = (int)value;
                return theme == 0 ? "dark" : "light";
            }
        }

        private void SetIcons()
        {
            
            string prefix = 0 < manualTheme.Length ? manualTheme : systemTheme;
            ResourceManager rm = Resources.ResourceManager;
            int capacity = 5;
            int death = 0;
            foreach (var v in map )
            {
                capacity = runner.Equals(v.Key) ? v.Value : capacity;
                death = v.Key.Equals(runner + "d") ? v.Value : death;
            }
            List<Icon> list = new List<Icon>(capacity);
            for (int i = 0; i < capacity; i++)
            {
                list.Add((Icon)rm.GetObject($"{prefix}_{runner}_{i}"));
            }

            if (death > 1)
            {
                List<Icon> listD = new List<Icon>(death);
                for (int i = 0; i < death; i++)
                {
                    listD.Add((Icon)rm.GetObject($"{prefix}_{runner + "d"}_{i}"));
                }
                iconsD = listD.ToArray();
            }
            iconsNomel = list.ToArray();
            icons = list.ToArray();
        }
        
        private void SetMonitors()
        {
            monitorCount = monitor.Equals("cpu") ? 0 : 1;
        }

        private void UpdateCheckedState(ToolStripMenuItem sender, ToolStripMenuItem menu)
        {
            foreach (ToolStripMenuItem item in menu.DropDownItems)
            {
                item.Checked = false;
            }
            sender.Checked = true;
        }

        private void SetRunner(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            UpdateCheckedState(item, runnerMenu);
            runner = item.Text.ToLower();
            SetIcons();
        }
            
        private void SetMonitor(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            UpdateCheckedState(item, rangeMenu);
            monitor = item.Text.ToLower();
            SetMonitors();
        }

        private void SetThemeIcons(object sender, EventArgs e)
        {
            UpdateCheckedState((ToolStripMenuItem)sender, themeMenu);
            manualTheme = "";
            systemTheme = GetAppsUseTheme();
            SetIcons();
        }

        private void UpdateThemeIcons()
        {
            if (0 < manualTheme.Length)
            {
                SetIcons();
                return;
            }
            string newTheme = GetAppsUseTheme();
            if (systemTheme.Equals(newTheme)) return;
            systemTheme = newTheme;
            SetIcons();
            SetMonitors();
        }

        private void SetLightIcons(object sender, EventArgs e)
        {
            UpdateCheckedState((ToolStripMenuItem)sender, themeMenu);
            manualTheme = "light";
            SetIcons();
        }

        private void SetDarkIcons(object sender, EventArgs e)
        {
            UpdateCheckedState((ToolStripMenuItem)sender, themeMenu);
            manualTheme = "dark";
            SetIcons();
        }
        private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General) UpdateThemeIcons();
        }

        private void SetStartup(object sender, EventArgs e)
        {
            startupMenu.Checked = !startupMenu.Checked;
            string keyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (RegistryKey rKey = Registry.CurrentUser.OpenSubKey(keyName, true))
            {
                if (startupMenu.Checked)
                {
                    rKey.SetValue(Application.ProductName, Process.GetCurrentProcess().MainModule.FileName);
                }
                else
                {
                    rKey.DeleteValue(Application.ProductName, false);
                }
                rKey.Close();
            }
        }

        private void Exit(object sender, EventArgs e)
        {
            cpuUsage.Close();
            memoryUsage.Close();
            animateTimer.Stop();
            cpuTimer.Stop();
            notifyIcon.Visible = false;
            Application.Exit();
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            if (icons.Length <= current) current = 0;
            notifyIcon.Icon = icons[current];
            current = (current + 1) % icons.Length;
        }

        private void SetAnimation()
        {
            animateTimer.Interval = ANIMATE_TIMER_DEFAULT_INTERVAL;
            animateTimer.Tick += new EventHandler(AnimationTick);
        }

        private void CPUTick()
        {
            float s = cpuUsage.NextValue();
            float used = total - 1024.0f * 1024.0f * memoryUsage.NextValue();
            float t = used / total * 100.0f;
            notifyIcon.Text = $"CPU: {s:f1}%\n Mem: {t:f2}%";
            s = ANIMATE_TIMER_DEFAULT_INTERVAL / (float)Math.Max(1.0f, Math.Min(20.0f, s / 5.0f));
            t = ANIMATE_TIMER_DEFAULT_INTERVAL / (float)Math.Max(1.0f, Math.Min(20.0f, t / 5.0f));
            int a =(int)(monitorCount > 0 ? t : s);
            icons = (iconsD.Length > 0 && a  < ANIMATE_TIMER_DEFAULT_INTERVAL/16) ? iconsD : iconsNomel;
            animateTimer.Stop();
            animateTimer.Interval =  a < 1 ? 1 : a;
            animateTimer.Start();
        }

        private void ObserveCPUTick(object sender, EventArgs e)
        {
            CPUTick();
        }

        private void StartObserveCPU()
        {
            cpuTimer.Interval = CPU_TIMER_DEFAULT_INTERVAL;
            cpuTimer.Tick += new EventHandler(ObserveCPUTick);
            cpuTimer.Start();
        }
        
        private void HandleDoubleClick(object Sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("taskmgr.exe");
        }

    }
}
