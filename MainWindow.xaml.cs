﻿using System.Diagnostics;
using System.Reflection;
using System.Windows;
using WpfMaiTouchEmulator.Managers;

namespace WpfMaiTouchEmulator;

public enum BorderSetting
{
    Disabled,
    Solid,
    Rainbow
}

public partial class MainWindow : Window
{
    private readonly MaiTouchSensorButtonStateManager buttonState;
    private readonly MaiTouchComConnector connector;
    private readonly VirtualComPortManager comPortManager;
    private TouchPanel? _touchPanel;

    public MainWindow()
    {
        InitializeComponent();
        CheckForUserSettingsUpgrade();
        DataContext = new MainWindowViewModel()
        {
            IsDebugEnabled = Properties.Settings.Default.IsDebugEnabled,
            IsAutomaticPortConnectingEnabled = Properties.Settings.Default.IsAutomaticPortConnectingEnabled,
            IsAutomaticPositioningEnabled = Properties.Settings.Default.IsAutomaticPositioningEnabled,
            IsExitWithSinmaiEnabled = Properties.Settings.Default.IsExitWithSinmaiEnabled,
            IsRingButtonEmulationEnabled = Properties.Settings.Default.IsRingButtonEmulationEnabled,
            IsLargeButtonsEnabled = Properties.Settings.Default.IsLargeButtonsEnabled,
            BorderColour = Properties.Settings.Default.BorderColour,
            LbAppVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0",
        };

        LoadBorderRadioButtonSetting();

        Title = "Mai Touch Emulator";
        buttonState = new MaiTouchSensorButtonStateManager(buttonStateValue);
        connector = new MaiTouchComConnector(buttonState, (MainWindowViewModel)DataContext);
        comPortManager = new VirtualComPortManager((MainWindowViewModel)DataContext);
        connector.OnConnectStatusChange = (status) =>
        {
            connectionStateLabel.Content = status;
        };

        connector.OnConnectError = () =>
        {
            var dataContext = (MainWindowViewModel)DataContext;
            dataContext.IsAutomaticPortConnectingEnabled = false;
        };

        connector.OnDataSent = (data) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                SentLogBox.AppendText(data + Environment.NewLine);
                SentLogBox.ScrollToEnd();
            });
        };
        connector.OnDataRecieved = (data) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                RecievedLogBox.AppendText(data + Environment.NewLine);
                RecievedLogBox.ScrollToEnd();
            });
        };

        if (Properties.Settings.Default.FirstOpen)
        {
            Logger.Info("First open occurred");
            ShowSetupInstructionsDialog();
            Properties.Settings.Default.FirstOpen = false;
            Properties.Settings.Default.Save();
        }


        Loaded += (s, e) => {
            var dataContext = (MainWindowViewModel)DataContext;

            Logger.Info("Main window loaded, creating touch panel");
            _touchPanel = new TouchPanel();
            _touchPanel.onTouch = (value) => { 
                buttonState.PressButton(value);
                connector.SendTouchscreenState();
            };
            _touchPanel.onRelease = (value) => {
                buttonState.ReleaseButton(value);
                connector.SendTouchscreenState();
            };
            _touchPanel.onInitialReposition = () => { WindowState = WindowState.Minimized; };
            _touchPanel.SetBorderMode((BorderSetting)Properties.Settings.Default.BorderSetting, dataContext.BorderColour);
            _touchPanel.Show();

            _touchPanel.DataContext = dataContext;

            _touchPanel.SetDebugMode(dataContext.IsDebugEnabled);
            _touchPanel.SetLargeButtonMode(dataContext.IsLargeButtonsEnabled);
            if (Properties.Settings.Default.IsAutomaticPositioningEnabled)
            {
                _touchPanel.DragWindowHandle.Visibility = Visibility.Hidden;
                _touchPanel.ResizeGrip.Visibility = Visibility.Hidden;
            }
            AutomaticTouchPanelPositioningLoop();
            AutomaticPortConnectingLoop();
            ExitWithSinmaiLoop();
        };
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        await connector.Disconnect();
        _touchPanel?.Close();
        Closing -= MainWindow_Closing;
        e.Cancel = false;
        Application.Current.Shutdown();
    }

    private async void ExitWithSinmaiLoop()
    {
        Process? sinamiProcess = null;
        while (sinamiProcess == null)
        {
            var processes = Process.GetProcessesByName("Sinmai");
            if (processes.Length > 0)
            {
                Logger.Info("Found sinmai process to exit alongside with");
                sinamiProcess = processes[0];
            }
            else
            {
                await Task.Delay(1000);
            }
        }
        var dataContext = (MainWindowViewModel)DataContext;

        if (dataContext.IsExitWithSinmaiEnabled)
        {
            try
            {
                await sinamiProcess.WaitForExitAsync();
                Logger.Info("Sinmai exited");
                if (dataContext.IsExitWithSinmaiEnabled)
                {
                    Logger.Info("Disconnecting from COM port before shutting down");
                    await connector.Disconnect();
                    Logger.Info("Shutting down...");
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to wait for sinmai to exit", ex);
                dataContext.IsExitWithSinmaiEnabled = false;
                Properties.Settings.Default.IsExitWithSinmaiEnabled = dataContext.IsExitWithSinmaiEnabled;
                Properties.Settings.Default.Save();
                MessageBox.Show(dataContext.TxtFailedToSetupSinmaiExit, dataContext.TxtFailedToSetupSinmaiExitHeader, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private static void CheckForUserSettingsUpgrade()
    {
        if (!Properties.Settings.Default.UserSettingsUpgradeRequired)
        {
            return;
        }

        Properties.Settings.Default.Upgrade();
        Properties.Settings.Default.UserSettingsUpgradeRequired = false;
        Properties.Settings.Default.Save();
    }

    private void ShowSetupInstructionsDialog()
    {
        var dataContext = (MainWindowViewModel)DataContext;
        MessageBox.Show(dataContext.TxtSetupInstructions, dataContext.TxtSetupInstructionsHeader, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void AutomaticTouchPanelPositioningLoop()
    {
        var dataContext = (MainWindowViewModel)DataContext;
        while (true)
        {
            if (dataContext.IsAutomaticPositioningEnabled)
            {
                _touchPanel?.PositionTouchPanel();
            }
        
            await Task.Delay(1000);
        }
    }

    private async void AutomaticPortConnectingLoop()
    {
        var dataContext = (MainWindowViewModel)DataContext;
        while (true)
        {
            if (dataContext.IsAutomaticPortConnectingEnabled)
            {
                connector.StartTouchSensorPolling();
            }
            await Task.Delay(1000);
        }
    }
    

    private void ConnectToPortButton_Click(object sender, RoutedEventArgs e)
    {
        connector.StartTouchSensorPolling();
    }

    private void debugMode_Click(object sender, RoutedEventArgs e)
    {
        var dataContext = (MainWindowViewModel)DataContext;
        var enabled = !dataContext.IsDebugEnabled;
        dataContext.IsDebugEnabled = !enabled;
        Properties.Settings.Default.IsDebugEnabled = dataContext.IsDebugEnabled;
        Properties.Settings.Default.Save();
        _touchPanel?.SetDebugMode(dataContext.IsDebugEnabled);
    }

    private void largeButtons_Click(object sender, RoutedEventArgs e)
    {
        var dataContext = (MainWindowViewModel)DataContext;
        var enabled = !dataContext.IsLargeButtonsEnabled;
        dataContext.IsLargeButtonsEnabled = !enabled;
        Properties.Settings.Default.IsLargeButtonsEnabled = dataContext.IsLargeButtonsEnabled;
        Properties.Settings.Default.Save();
        _touchPanel?.SetLargeButtonMode(dataContext.IsLargeButtonsEnabled);
    }

    private void automaticTouchPanelPositioning_Click(object sender, RoutedEventArgs e)
    {
        var dataContext = (MainWindowViewModel)DataContext;
        var enabled = !dataContext.IsAutomaticPositioningEnabled;
        dataContext.IsAutomaticPositioningEnabled = !enabled;

        _touchPanel.DragWindowHandle.Visibility = dataContext.IsAutomaticPositioningEnabled ? Visibility.Hidden : Visibility.Visible;
        _touchPanel.ResizeGrip.Visibility = dataContext.IsAutomaticPositioningEnabled ? Visibility.Hidden : Visibility.Visible;

        Properties.Settings.Default.IsAutomaticPositioningEnabled = dataContext.IsAutomaticPositioningEnabled;
        Properties.Settings.Default.Save();
    }

    private void automaticPortConnecting_Click(object sender, RoutedEventArgs e)
    {
        var dataContext = (MainWindowViewModel)DataContext;
        var enabled = !dataContext.IsAutomaticPortConnectingEnabled;
        dataContext.IsAutomaticPortConnectingEnabled = !enabled;
        Properties.Settings.Default.IsAutomaticPortConnectingEnabled = dataContext.IsAutomaticPortConnectingEnabled;
        Properties.Settings.Default.Save();
    }

    private void exitWithSinmai_Click(object sender, RoutedEventArgs e)
    {
        var dataContext = (MainWindowViewModel)DataContext;
        var enabled = !dataContext.IsExitWithSinmaiEnabled;
        dataContext.IsExitWithSinmaiEnabled = !enabled;
        Properties.Settings.Default.IsExitWithSinmaiEnabled = dataContext.IsExitWithSinmaiEnabled;
        Properties.Settings.Default.Save();
    }

    private async void buttonInstallComPort_Click(object sender, RoutedEventArgs e)
    {
        await comPortManager.InstallComPort();
    }

    private async void buttonUninstallComPorts_Click(object sender, RoutedEventArgs e)
    {
        await comPortManager.UninstallVirtualPorts();
    }

    private void buttonListComPorts_Click(object sender, RoutedEventArgs e)
    {
        var output = comPortManager.GetInstalledPorts();
        var dataContext = (MainWindowViewModel)DataContext;
        MessageBox.Show(string.Join("\n", output), dataContext.TxtCurrentlyInstalledPorts);
    }

    private void instructionsLabel_Click(object sender, RoutedEventArgs e)
    {
        ShowSetupInstructionsDialog();
    }

    private void emulateRingButtons_Click(object sender, RoutedEventArgs e)
    {
        var dataContext = (MainWindowViewModel)DataContext;
        var enabled = !dataContext.IsRingButtonEmulationEnabled;
        dataContext.IsRingButtonEmulationEnabled = !enabled;
        Properties.Settings.Default.IsRingButtonEmulationEnabled = dataContext.IsRingButtonEmulationEnabled;
        Properties.Settings.Default.Save();
        _touchPanel?.SetEmulateRingButton(dataContext.IsRingButtonEmulationEnabled);
    }

    private void LoadBorderRadioButtonSetting()
    {
        rbBorderDisabled.IsChecked = Properties.Settings.Default.BorderSetting == (int)BorderSetting.Disabled;
        txtBorderHexColor.IsEnabled = Properties.Settings.Default.BorderSetting == (int)BorderSetting.Solid;
        rbBorderSolidColour.IsChecked = Properties.Settings.Default.BorderSetting == (int)BorderSetting.Solid;
        rbBorderRainbow.IsChecked = Properties.Settings.Default.BorderSetting == (int)BorderSetting.Rainbow;
    }

    private void RadioButton1_Checked(object sender, RoutedEventArgs e)
    {
        txtBorderHexColor.IsEnabled = false;
        Properties.Settings.Default.BorderSetting = (int)BorderSetting.Disabled;
        Properties.Settings.Default.Save();

        _touchPanel?.SetBorderMode(BorderSetting.Disabled, "");
    }

    private void RadioButton2_Checked(object sender, RoutedEventArgs e)
    {
        txtBorderHexColor.IsEnabled = true;
        Properties.Settings.Default.BorderSetting = (int)BorderSetting.Solid;

        Properties.Settings.Default.Save();

        _touchPanel?.SetBorderMode(BorderSetting.Solid, Properties.Settings.Default.BorderColour);
    }

    private void RadioButton3_Checked(object sender, RoutedEventArgs e)
    {
        txtBorderHexColor.IsEnabled = false;
        Properties.Settings.Default.BorderSetting = (int)BorderSetting.Rainbow;

        Properties.Settings.Default.Save();

        _touchPanel?.SetBorderMode(BorderSetting.Rainbow, "");
    }

    private void txtBorderHexColor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var textWithoutHash = txtBorderHexColor.Text.TrimStart('#') ?? "";
        if (textWithoutHash.Length == 6 || textWithoutHash.Length == 8)
        {
            var textWithHash = "#" + textWithoutHash;
            Properties.Settings.Default.BorderColour = textWithHash;
            Properties.Settings.Default.Save();
            _touchPanel?.SetBorderMode(BorderSetting.Solid, textWithHash);
        }
    }

    private void openLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", Logger.GetLogPath());
    }
}
