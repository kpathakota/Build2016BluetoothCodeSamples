//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************

using System;
using System.Threading;
using System.Collections.Generic;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using System.ComponentModel;
using Windows.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;
using Windows.UI.Xaml.Data;
using Windows.Storage.Streams;

namespace SDKTemplate
{
    public sealed partial class Scenario1_InAppGATT : Page
    {
        // A pointer back to the main page is required to display status messages.
        private MainPage rootPage = MainPage.Current;

        // Used to display list of available devices 
        public ObservableCollection<BluetoothLEDeviceDisplay> ResultCollection
        {
            get;
            private set;
        }

        private DeviceWatcher deviceWatcher = null;
        private DeviceWatcher gattServiceWatcher = null;
        private static Guid IoServiceUuid = new Guid("00004F0E-1212-efde-1523-785feabcd123");
        private static Guid OutputCommandCharacteristicGuid = new Guid("00001565-1212-efde-1523-785feabcd123");
        private GattDeviceService weDoIoService = null;
        private GattCharacteristic outputCommandCharacteristic = null;

        public Scenario1_InAppGATT()
        {
            this.InitializeComponent();
            App.Current.Suspending += App_Suspending;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            rootPage = MainPage.Current;
            ResultCollection = new ObservableCollection<BluetoothLEDeviceDisplay>();
            DataContext = this;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            StopBleDeviceWatcher();
            StopGattServiceWatcher();
        }

        private void StopBleDeviceWatcher()
        {
            StopWatcherButton.IsEnabled = false;

            if (null != deviceWatcher)
            {
                if (DeviceWatcherStatus.Started == deviceWatcher.Status ||
                    DeviceWatcherStatus.EnumerationCompleted == deviceWatcher.Status)
                {
                    deviceWatcher.Stop();
                }
            }

            StartWatcherButton.IsEnabled = true;
        }

        void App_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            // Make sure we clean up resources on suspend.
            StopBleDeviceWatcher();
            StopGattServiceWatcher();
        }

        /// <summary>
        /// When the user presses the Start Watcher button, query for all Bluetooth LE devices
        /// </summary>
        /// <param name="sender">Instance that triggered the event.</param>
        /// <param name="e">Event data describing the conditions that led to the event.</param>
        private void StartWatcherButton_Click(object sender, RoutedEventArgs e)
        {
            // Disable the button while the watcher is active so the user can't run twice.
            var button = sender as Button;
            button.IsEnabled = false;
            StopWatcherButton.IsEnabled = true;

            // Clear any previous messages
            rootPage.NotifyUser("", NotifyType.StatusMessage);

            // Enumerate all Bluetooth LE devices and display them in a list
            StartBleDeviceWatcher();
        }
        private void StopWatcherButton_Click(object sender, RoutedEventArgs e)
        {
            StopBleDeviceWatcher();
        }

        private void StartBleDeviceWatcher()
        {
            //Reset displayed results
            ResultCollection.Clear();

            // Request additional properties
            string[] requestedProperties = new string[] {"System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected"};

            deviceWatcher = DeviceInformation.CreateWatcher("(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")", 
                                                            requestedProperties, 
                                                            DeviceInformationKind.AssociationEndpoint);

            // Hook up handlers for the watcher events before starting the watcher
            deviceWatcher.Added += new TypedEventHandler<DeviceWatcher, DeviceInformation>(async (watcher, deviceInfo) =>
            {
                // Since we have the collection databound to a UI element, we need to update the collection on the UI thread.
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    ResultCollection.Add(new BluetoothLEDeviceDisplay(deviceInfo));

                    rootPage.NotifyUser(
                        String.Format("{0} devices found.", ResultCollection.Count),
                        NotifyType.StatusMessage);
                });
            });

            deviceWatcher.Updated += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
            {
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    foreach (BluetoothLEDeviceDisplay bleInfoDisp in ResultCollection)
                    {
                        if (bleInfoDisp.Id == deviceInfoUpdate.Id)
                        {
                            bleInfoDisp.Update(deviceInfoUpdate);

                            // If the item being updated is currently "selected", then update the pairing buttons
                            BluetoothLEDeviceDisplay selectedDeviceInfoDisp = (BluetoothLEDeviceDisplay)resultsListView.SelectedItem;
                            if (bleInfoDisp == selectedDeviceInfoDisp)
                            {
                                UpdateButtons();
                            }

                            break;
                        }
                    }
                });
            });

            deviceWatcher.EnumerationCompleted += new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
            {
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    rootPage.NotifyUser(
                        String.Format("{0} devices found. Enumeration completed. Watching for updates...", ResultCollection.Count),
                        NotifyType.StatusMessage);
                });
            });

            deviceWatcher.Removed += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
            {
                // Since we have the collection databound to a UI element, we need to update the collection on the UI thread.
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    // Find the corresponding DeviceInformation in the collection and remove it
                    foreach (BluetoothLEDeviceDisplay bleInfoDisp in ResultCollection)
                    {
                        if (bleInfoDisp.Id == deviceInfoUpdate.Id)
                        {
                            ResultCollection.Remove(bleInfoDisp);
                            break;
                        }
                    }

                    rootPage.NotifyUser(
                        String.Format("{0} devices found.", ResultCollection.Count),
                        NotifyType.StatusMessage);
                });
            });

            deviceWatcher.Stopped += new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
            {
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    rootPage.NotifyUser(
                        String.Format("{0} devices found. Watcher {1}.",
                            ResultCollection.Count,
                            DeviceWatcherStatus.Aborted == watcher.Status ? "aborted" : "stopped"),
                        NotifyType.StatusMessage);
                });
            });

            deviceWatcher.Start();
        }

        private void StopGattServiceWatcher()
        {
            if (null != gattServiceWatcher)
            {
                if (DeviceWatcherStatus.Started == gattServiceWatcher.Status ||
                    DeviceWatcherStatus.EnumerationCompleted == gattServiceWatcher.Status)
                {
                    gattServiceWatcher.Stop();
                }
            }
        }

        private async void StartGattServiceWatcher()
        {
            //Get the Bluetooth address for filtering the watcher
            BluetoothLEDeviceDisplay deviceInfoDisp = resultsListView.SelectedItem as BluetoothLEDeviceDisplay;
            BluetoothLEDevice bleDevice = await BluetoothLEDevice.FromIdAsync(deviceInfoDisp.Id);
            string selector = "(" + GattDeviceService.GetDeviceSelectorFromUuid(IoServiceUuid) + ")" 
                                + " AND (System.DeviceInterface.Bluetooth.DeviceAddress:=\"" 
                                + bleDevice.BluetoothAddress.ToString("X") + "\")";

            gattServiceWatcher = DeviceInformation.CreateWatcher(selector);

            // Hook up handlers for the watcher events before starting the watcher
            gattServiceWatcher.Added += new TypedEventHandler<DeviceWatcher, DeviceInformation>(async (watcher, deviceInfo) =>
            {
                // If the selected device is a WeDo device, enable the controls
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    weDoIoService = await GattDeviceService.FromIdAsync(deviceInfo.Id);
                    outputCommandCharacteristic = weDoIoService.GetCharacteristics(OutputCommandCharacteristicGuid)[0];
                    ForwardButton.IsEnabled = true;
                    StopButton.IsEnabled = true;
                    BackwardButton.IsEnabled = true;
                });
            });

            gattServiceWatcher.Updated += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
            {
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    //Do nothing
                });
            });

            gattServiceWatcher.EnumerationCompleted += new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
            {
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    //Do nothing
                });
            });

            gattServiceWatcher.Removed += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
            {
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    //Do nothing
                });
            });

            gattServiceWatcher.Stopped += new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
            {
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    //Do nothing
                });
            });

            gattServiceWatcher.Start();
        }

        private void ResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtons();
        }

        private async void PairButton_Click(object sender, RoutedEventArgs e)
        {
            // Gray out the pair button and results view while pairing is in progress.
            resultsListView.IsEnabled = false;
            PairButton.IsEnabled = false;
            rootPage.NotifyUser("Pairing started. Please wait...", NotifyType.StatusMessage);

            // Get the device selected for pairing
            BluetoothLEDeviceDisplay deviceInfoDisp = resultsListView.SelectedItem as BluetoothLEDeviceDisplay;
            DevicePairingResult result = null;

            result = await deviceInfoDisp.DeviceInformation.Pairing.PairAsync();

            rootPage.NotifyUser(
                "Pairing result = " + result.Status.ToString(),
                result.Status == DevicePairingResultStatus.Paired ? NotifyType.StatusMessage : NotifyType.ErrorMessage);

            UpdateButtons();
            resultsListView.IsEnabled = true;
        }

        private async void UnpairButton_Click(object sender, RoutedEventArgs e)
        {
            // Gray out the unpair button and results view while unpairing is in progress.
            resultsListView.IsEnabled = false;
            UnpairButton.IsEnabled = false;
            rootPage.NotifyUser("Unpairing started. Please wait...", NotifyType.StatusMessage);

            BluetoothLEDeviceDisplay deviceInfoDisp = resultsListView.SelectedItem as BluetoothLEDeviceDisplay;

            DeviceUnpairingResult dupr = await deviceInfoDisp.DeviceInformation.Pairing.UnpairAsync();

            rootPage.NotifyUser(
                "Unpairing result = " + dupr.Status.ToString(),
                dupr.Status == DeviceUnpairingResultStatus.Unpaired ? NotifyType.StatusMessage : NotifyType.ErrorMessage);

            UpdateButtons();
            resultsListView.IsEnabled = true;
        }

        private async void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            DataWriter writer = new DataWriter();
            byte[] data = new byte[] 
                {
                    1, // connectId - 1/2
                    1, // commandId - 1 is for writing motor power
                    1, // data size in bytes
                    100  // data, in this case - motor power - (100) full speed forward 
                };
            writer.WriteBytes(data);

            GattCommunicationStatus status = await outputCommandCharacteristic.WriteValueAsync(
                writer.DetachBuffer());
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            DataWriter writer = new DataWriter();
            byte[] data = new byte[] 
                {
                    1, // connectId - 1/2
                    1, // commandId - 1 is for writing motor power
                    1, // data size in bytes
                    0  // data, in this case - motor power - (0) stop
                };
            writer.WriteBytes(data);

            GattCommunicationStatus status = await outputCommandCharacteristic.WriteValueAsync(
                writer.DetachBuffer());
        }

        private async void BackwardButton_Click(object sender, RoutedEventArgs e)
        {
            DataWriter writer = new DataWriter();
            byte[] data = new byte[]
                {
                    1, // connectId - 1/2
                    1, // commandId - 1 is for writing motor power
                    1, // data size in bytes
                    0x9C  // data, in this case - motor power - (-100) full speed backward
                };
            writer.WriteBytes(data);

            GattCommunicationStatus status = await outputCommandCharacteristic.WriteValueAsync(
                writer.DetachBuffer());
        }

        private void UpdateButtons()
        {
            BluetoothLEDeviceDisplay deviceInfoDisp = (BluetoothLEDeviceDisplay)resultsListView.SelectedItem;

            //Reset the device control buttons to disabled
            ForwardButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            BackwardButton.IsEnabled = false;

            //Update pairing buttons based on IsPaired state
            if (null != deviceInfoDisp && !deviceInfoDisp.DeviceInformation.Pairing.IsPaired)
            {
                PairButton.IsEnabled = true;
            }
            else
            {
                PairButton.IsEnabled = false;
            }

            if (null != deviceInfoDisp &&
                deviceInfoDisp.DeviceInformation.Pairing.IsPaired)
            {
                UnpairButton.IsEnabled = true;
            }
            else
            {
                UnpairButton.IsEnabled = false;
            }

            //Stop any existing service watcher
            if (gattServiceWatcher != null)
            {
                StopGattServiceWatcher();
            }

            //If there is a paired device selected, look for the WeDo service and enable controls if found
            if (deviceInfoDisp != null)
            {
                if (deviceInfoDisp.IsPaired == true)
                {
                    StartGattServiceWatcher();
                }
            }

        }
    }

    public class BluetoothLEDeviceDisplay : INotifyPropertyChanged
    {
        private DeviceInformation deviceInfo;

        public BluetoothLEDeviceDisplay(DeviceInformation deviceInfoIn)
        {
            deviceInfo = deviceInfoIn;
            UpdateGlyphBitmapImage();
        }

        public DeviceInformation DeviceInformation
        {
            get
            {
                return deviceInfo;
            }

            private set
            {
                deviceInfo = value;
            }
        }

        public string Id
        {
            get
            {
                return deviceInfo.Id;
            }
        }

        public string Name
        {
            get
            {
                return deviceInfo.Name;
            }
        }
        public bool IsPaired
        {
            get
            {
                return deviceInfo.Pairing.IsPaired;
            }
        }

        public IReadOnlyDictionary<string, object> Properties
        {
            get
            {
                return deviceInfo.Properties;
            }
        }

        public BitmapImage GlyphBitmapImage
        {
            get;
            private set;
        }

        public void Update(DeviceInformationUpdate deviceInfoUpdate)
        {
            deviceInfo.Update(deviceInfoUpdate);

            OnPropertyChanged("Id");
            OnPropertyChanged("Name");
            OnPropertyChanged("DeviceInformation");
            OnPropertyChanged("IsPaired");
            OnPropertyChanged("Properties");

            UpdateGlyphBitmapImage();
        }

        private async void UpdateGlyphBitmapImage()
        {
            DeviceThumbnail deviceThumbnail = await deviceInfo.GetGlyphThumbnailAsync();
            BitmapImage glyphBitmapImage = new BitmapImage();
            await glyphBitmapImage.SetSourceAsync(deviceThumbnail);
            GlyphBitmapImage = glyphBitmapImage;
            OnPropertyChanged("GlyphBitmapImage");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    public class GeneralPropertyValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            object property = null;

            if (value is IReadOnlyDictionary<string, object> &&
                parameter is string &&
                !String.IsNullOrEmpty((string)parameter))
            {
                IReadOnlyDictionary<string, object> properties = value as IReadOnlyDictionary<string, object>;
                string propertyName = parameter as string;

                property = properties[propertyName];
            }

            return property;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
