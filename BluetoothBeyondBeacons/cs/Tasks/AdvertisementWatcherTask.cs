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
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Background;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace BackgroundTasks
{
    // A background task always implements the IBackgroundTask interface.
    public sealed class AdvertisementWatcherTask : IBackgroundTask
    {
        private IBackgroundTaskInstance backgroundTaskInstance;

        // GUID for the motor characteristic on the Lego device
        private static Guid OutputCommandCharacteristicGuid = new Guid("00001565-1212-efde-1523-785feabcd123");

        // GUID for the I/O service on the Lego device
        private static Guid IoServiceUuid = new Guid("00004F0E-1212-efde-1523-785feabcd123");

        // Min RSSI needed to activate the motor. 
        // Decrease this to activate the motor when the device is further away.
        private static int WriteCharacteristicMinRSSI = -40;

        // Number of seconds the motor will spin for
        private static int MotorSpinSeconds = 2;

        /// <summary>
        /// Write to the motor telling it to spin 
        /// </summary>
        /// <param name="outputCommandCharacteristic">The characteristic to write to.</param>
        private async Task<GattCommunicationStatus> writeToCharacteristic(GattCharacteristic outputCommandCharacteristic)
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
            var buf = writer.DetachBuffer();
            return await outputCommandCharacteristic.WriteValueAsync(buf);
        }

        /// <summary>
        /// The entry point of a background task.
        /// </summary>
        /// <param name="taskInstance">The current background task instance.</param>
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            backgroundTaskInstance = taskInstance;

            // Get the details of the trigger
            var details = taskInstance.TriggerDetails as BluetoothLEAdvertisementWatcherTriggerDetails;
            if (details != null)
            {
                // If the background watcher stopped unexpectedly, an error will be available here.
                var error = details.Error;

                if (error == BluetoothError.Success)
                {
                    // The Advertisements property is a list of all advertisement events received
                    // since the last task triggered. The list of advertisements here might be valid even if
                    // the Error status is not Success since advertisements are stored until this task is triggered
                    IReadOnlyList<BluetoothLEAdvertisementReceivedEventArgs> advertisements = details.Advertisements;

                    // The signal strength filter configuration of the trigger is returned such that further
                    // processing can be performed here using these values if necessary. They are read-only here.
                    var rssiFilter = details.SignalStrengthFilter;

                    // Make sure we have advertisements to work with
                    if (advertisements.Count == 0) return;

                    // Grab the first advertisement
                    var eventArg = advertisements[0];

                    if (eventArg.RawSignalStrengthInDBm > WriteCharacteristicMinRSSI)
                    {
                        // Get a deferral so we can use the await operator without the background task returning and closing the thread
                        BackgroundTaskDeferral deferral = taskInstance.GetDeferral();

                        // Get a connection to the device and get the service that we're looking for
                        BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(eventArg.BluetoothAddress);

                        // Get the service and characteristic we're looking for
                        var service = device.GetGattService(IoServiceUuid);
                        var characteristic = service.GetCharacteristics(OutputCommandCharacteristicGuid)[0];

                        // Write to the motor characteristic telling it to spin
                        GattCommunicationStatus status = await this.writeToCharacteristic(characteristic);

                        // Wait a couple seconds before we disconnect so we can see the motor spin
                        await Task.Delay(TimeSpan.FromSeconds(MotorSpinSeconds));

                        // Disconnect from the device so the motor stops
                        device.Dispose();

                        // Let the system know that we've finished the background task
                        deferral.Complete();
                    }
                }
            }
        }
    }
}
