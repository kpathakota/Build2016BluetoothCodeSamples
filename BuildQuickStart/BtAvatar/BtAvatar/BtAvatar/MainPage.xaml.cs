using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Core;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace BtAvatar
{
    
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private StreamSocket socket;
        private DataWriter writer;
        private RfcommServiceProvider rfcommProvider;
        private StreamSocketListener socketListener;
        private readonly Guid rfcommChatServiceUuid = Guid.Parse("87bf52c0-4595-43b3-a5b0-702de8d5dbba");

        public const UInt16 SdpServiceNameAttributeId = 0x100;
        public const byte SdpServiceNameAttributeType = (4 << 3) | 5;
        public const string SdpServiceName = "Sensor Avatar Service";

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {


           
        }

        private void ListenButton_Click(object sender, RoutedEventArgs e)
        {
            InitializeRfcommServer();
        }
        /// <summary>
        /// Initializes the server using RfcommServiceProvider to advertise the Chat Service UUID and start listening
        /// for incoming connections.
        /// </summary>
        private async void InitializeRfcommServer()
        {

            try
            {
                rfcommProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(rfcommChatServiceUuid));
            }
            catch (Exception e)
            {
                // If you aren't able to get a reference to an RfcommServiceProvider, tell the user why.  Usually throws an exception if the Bluetooth radio is off.  
                Status.Text = e.Message;
                return;
            }


            // Create a listener for this service and start listening
            socketListener = new StreamSocketListener();
            socketListener.ConnectionReceived += OnConnectionReceived;

            await socketListener.BindServiceNameAsync(rfcommProvider.ServiceId.AsString(),
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

            // Set the SDP attributes and start Bluetooth advertising
            InitializeServiceSdpAttributes(rfcommProvider);

            try
            {
                rfcommProvider.StartAdvertising(socketListener, true);
            }
            catch (Exception e)
            {
                Status.Text = e.Message;
                return;
            }


            Status.Text = "Listening for incoming connections";
        }

        /// <summary>
        /// Creates the SDP record that will be revealed to the Client device when pairing occurs.  
        /// </summary>
        /// <param name="rfcommProvider">The RfcommServiceProvider that is being used to initialize the server</param>
        private void InitializeServiceSdpAttributes(RfcommServiceProvider rfcommProvider)
        {
            var sdpWriter = new DataWriter();

            // Write the Service Name Attribute.
            sdpWriter.WriteByte(SdpServiceNameAttributeType);

            // The length of the UTF-8 encoded Service Name SDP Attribute.
            sdpWriter.WriteByte((byte)SdpServiceName.Length);

            // The UTF-8 encoded Service Name value.
            sdpWriter.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            sdpWriter.WriteString(SdpServiceName);

            // Set the SDP Attribute on the RFCOMM Service Provider.
            rfcommProvider.SdpRawAttributes.Add(SdpServiceNameAttributeId, sdpWriter.DetachBuffer());
        }

        /// <summary>
        /// Invoked when the socket listener accepts an incoming Bluetooth connection.
        /// </summary>
        /// <param name="sender">The socket listener that accepted the connection.</param>
        /// <param name="args">The connection accept parameters, which contain the connected socket.</param>
        private async void OnConnectionReceived(
            StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            // Don't need the listener anymore
            socketListener.Dispose();
            socketListener = null;

            try
            {
                socket = args.Socket;
            }
            catch (Exception e)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    Status.Text = e.Message;
                });
                Disconnect();
                return;
            }

            // Note - this is the supported way to get a Bluetooth device from a given socket
            var remoteDevice = await BluetoothDevice.FromHostNameAsync(socket.Information.RemoteHostName);

            writer = new DataWriter(socket.OutputStream);
            var reader = new DataReader(socket.InputStream);
            var remoteDisconnection = false;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Status.Text = "Connected to Client: " + remoteDevice.Name;
            });

            // Infinite read buffer loop
            while (true)
            {
                try
                {
                    // Based on the protocol we've defined, the first uint is the size of the message
                    uint readLength = await reader.LoadAsync(sizeof(uint));

                    // Check if the size of the data is expected (otherwise the remote has already terminated the connection)
                    if (readLength < sizeof(uint))
                    {
                        remoteDisconnection = true;
                        break;
                    }
                    uint currentLength = reader.ReadUInt32();

                    // Load the rest of the message since you already know the length of the data expected.  
                    readLength = await reader.LoadAsync(currentLength);

                    // Check if the size of the data is expected (otherwise the remote has already terminated the connection)
                    if (readLength < currentLength)
                    {
                        remoteDisconnection = true;
                        break;
                    }
                    string message = reader.ReadString(currentLength);

                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {

                        switch (message)
                        {
                            case ("Walking"):
                                mediaElement.Pause();
                                mediaElement.Play();
                                mediaElement.PlaybackRate = 1.0;
                                break;
                            case ("Running"):
                                mediaElement.Pause();
                                mediaElement.Play();
                                mediaElement.PlaybackRate = 1.5;
                                break;
                            case ("Stationary"):
                                mediaElement.Pause();
                                break;
                            default:
                                mediaElement.Pause();
                                break;
                        }

                        Status.Text = "Activity: " + message + "PlaybackRate= " + mediaElement.PlaybackRate;
                    });


                }
                catch (Exception ex)
                {
                    Status.Text = ex.Message + "Remote Disconnection: " + remoteDisconnection;

                }
            }
        }

        private void Disconnect()
        {
            if (rfcommProvider != null)
            {
                rfcommProvider.StopAdvertising();
                rfcommProvider = null;
            }

            if (socketListener != null)
            {
                socketListener.Dispose();
                socketListener = null;
            }

            if (writer != null)
            {
                writer.DetachStream();
                writer = null;
            }

            if (socket != null)
            {
                socket.Dispose();
                socket = null;
            }
        }

        private async void ChooseAvatar_Click(object sender, RoutedEventArgs e)
        {
            // Create and open the file picker
            FileOpenPicker openPicker = new FileOpenPicker();
            openPicker.ViewMode = PickerViewMode.Thumbnail;
            openPicker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            openPicker.FileTypeFilter.Add(".mp4");
            openPicker.FileTypeFilter.Add(".mkv");
            openPicker.FileTypeFilter.Add(".avi");

            StorageFile file = await openPicker.PickSingleFileAsync();
            if (file != null)
            {
                Status.Text = "Picked video: " + file.Name;
                mediaElement.SetPlaybackSource(MediaSource.CreateFromStorageFile(file));
                mediaElement.Play();
            }
            else
            {
                Status.Text = "Operation cancelled";
            }
        }

        int clickNum = 0;
        private void TogglePlayState_Click(object sender, RoutedEventArgs e)
        {
            


            switch (clickNum % 3)
            {
                case (0):
                    mediaElement.Pause();
                    mediaElement.Play();
                    mediaElement.PlaybackRate = 1.0;
                    break;
                case (1):
                    mediaElement.Pause();
                    mediaElement.Play();
                    mediaElement.PlaybackRate = 2.0;
                    break;
                case (2):
                    mediaElement.Pause();
                    break;
                default:
                    mediaElement.Pause();
                    break;
            }

            System.Diagnostics.Debug.WriteLine("PlayState: " + mediaElement.CurrentState + " PlaybackRate: " + mediaElement.PlaybackRate);
            clickNum += 1;

        }
    }
}
