using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Sensors;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.ComponentModel;
using System.Collections.ObjectModel;
using Windows.UI.Core;
// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace AvatarSensor
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private DeviceWatcher deviceWatcher;
        private StreamSocket chatSocket;
        private DataWriter chatWriter;
        private RfcommDeviceService chatService;

        // Common class ID for activity sensors
        Guid ActivitySensorClassId = new Guid("9D9E0118-1807-4F2E-96E4-2CE57142E196");
        private ActivitySensor activitySensor;
        private readonly Guid avatarServiceUUID = Guid.Parse("87bf52c0-4595-43b3-a5b0-702de8d5dbba");

        // Used to display list of available devices to chat with
        public ObservableCollection<AvatarDeviceDisplay> ResultCollection
        {
            get;
            private set;
        }

        public MainPage()
        {
            this.InitializeComponent();

            ResultCollection = new ObservableCollection<AvatarDeviceDisplay>();
            DataContext = this;
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            Status.Text = "Looking for nearby devices";

            deviceWatcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(false), null);
            // Hook up handlers for the watcher events before starting the watcher
            deviceWatcher.Added += new TypedEventHandler<DeviceWatcher, DeviceInformation>(async (watcher, deviceInfo) =>
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Make sure device name isn't blank
                    if (deviceInfo.Name != "")
                    {
                        ResultCollection.Add(new AvatarDeviceDisplay(deviceInfo));
                        System.Diagnostics.Debug.WriteLine(resultsListView.Items.Count);

                    }

                });
            });

            deviceWatcher.Updated += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    foreach (AvatarDeviceDisplay avatarInfoDisp in ResultCollection)
                    {
                        if (avatarInfoDisp.Id == deviceInfoUpdate.Id)
                        {
                            avatarInfoDisp.Update(deviceInfoUpdate);
                            break;
                        }
                    }
                });
            });

            deviceWatcher.EnumerationCompleted += new TypedEventHandler<DeviceWatcher, Object>((watcher, obj) =>
            {

            });

            deviceWatcher.Removed += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Find the corresponding DeviceInformation in the collection and remove it
                    foreach (AvatarDeviceDisplay avatarInfoDisp in ResultCollection)
                    {
                        if (avatarInfoDisp.Id == deviceInfoUpdate.Id)
                        {
                            ResultCollection.Remove(avatarInfoDisp);
                            break;
                        }
                    }
                });
            });

            deviceWatcher.Stopped += new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Find the corresponding DeviceInformation in the collection and remove it
                    ResultCollection.Clear();
                    Status.Text = "Watcher Stopped";
                    RunButton.IsEnabled = true;
                });
                
            });

            deviceWatcher.Start();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {

            AvatarDeviceDisplay deviceInfoDisp = resultsListView.SelectedItem as AvatarDeviceDisplay;


            var bluetoothDevice = await BluetoothDevice.FromIdAsync(deviceInfoDisp.Id); ;

            var rfcommServices = await bluetoothDevice.GetRfcommServicesForIdAsync(
                RfcommServiceId.FromUuid(avatarServiceUUID), BluetoothCacheMode.Uncached);

            if (rfcommServices.Services.Count > 0)
            {
                chatService = rfcommServices.Services[0];
            }
            else
            {
                Status.Text = "Could not discover the avatar service on the remote device";
                return;
            }

            deviceWatcher.Stop();

   

            lock (this)
            {
                chatSocket = new StreamSocket();
            }
            try
            {
                await chatSocket.ConnectAsync(chatService.ConnectionHostName, chatService.ConnectionServiceName);

                chatWriter = new DataWriter(chatSocket.OutputStream);

                EnableActivityDetectionAsync();
            }
            catch (Exception ex)
            {
                switch ((uint)ex.HResult)
                {
                    case (0x80070490): // ERROR_ELEMENT_NOT_FOUND
                        Status.Text = "Please verify that you are running the Avatar server.";
                        RunButton.IsEnabled = true;
                        break;
                    default:
                        throw;
                }
            }

        }

        private async void EnableActivityDetectionAsync()
        {
            var deviceAccessInfo = DeviceAccessInformation.CreateFromDeviceClassId(ActivitySensorClassId);
            // Determine if we can access activity sensors
            if (deviceAccessInfo.CurrentStatus == DeviceAccessStatus.Allowed)
            {
                if (activitySensor == null)
                {
                    activitySensor = await ActivitySensor.GetDefaultAsync();
                }

                if (activitySensor != null)
                {
                    activitySensor.SubscribedActivities.Add(ActivityType.Walking);
                    activitySensor.SubscribedActivities.Add(ActivityType.Running);
                    activitySensor.SubscribedActivities.Add(ActivityType.Fidgeting);
                    activitySensor.SubscribedActivities.Add(ActivityType.Stationary);


                    activitySensor.ReadingChanged += new TypedEventHandler<ActivitySensor, ActivitySensorReadingChangedEventArgs>(ReadingChanged);

                    Status.Text = "Subscribed to reading changes";
                }
                else
                {
                    Status.Text = "No activity sensor found";
                }
            }
            else
            {
                Status.Text = "Access denied to activity sensors";
            }
        }

        private void DisableActivityDetection()
        {
            if (activitySensor != null)
            {
                activitySensor.ReadingChanged -= new TypedEventHandler<ActivitySensor, ActivitySensorReadingChangedEventArgs>(ReadingChanged);
                Status.Text = "Unsubscribed from reading changes";
            }
        }
        private async void ReadingChanged(ActivitySensor sender, ActivitySensorReadingChangedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ActivitySensorReading reading = args.Reading;

                SendMessage(reading.Activity.ToString());
            });
        }

        private async void SendMessage(string message)
        {
            try
            {
                if (message.Length != 0)
                {
                    chatWriter.WriteUInt32((uint)message.Length);
                    chatWriter.WriteString(message);

                    Status.Text = message;
                    await chatWriter.StoreAsync();

                }
            }
            catch (Exception ex)
            {
                Status.Text = "Error: " + ex.HResult.ToString() + " - " + ex.Message;
            }
        }

        int clickNum = 0;
        private void MessageToggle_Click(object sender, RoutedEventArgs e)
        {
            switch (clickNum % 3)
            {
                case (0):
                    SendMessage("Walking");
                    break;
                case (1):
                    SendMessage("Running");
                    break;
                case (2):
                    SendMessage("Stationary");
                    break;
                default:
                    SendMessage("Car");
                    break;
            }

            System.Diagnostics.Debug.WriteLine("Message sent: " + clickNum);
            clickNum += 1;
        }
    }

    public class AvatarDeviceDisplay : INotifyPropertyChanged
    {
        private DeviceInformation deviceInfo;

        public AvatarDeviceDisplay(DeviceInformation deviceInfoIn)
        {
            deviceInfo = deviceInfoIn;
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

        public void Update(DeviceInformationUpdate deviceInfoUpdate)
        {
            deviceInfo.Update(deviceInfoUpdate);
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
}
