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
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.ApplicationModel.Background;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace SDKTemplate
{
    public sealed partial class Scenario1_BeaconProximity : Page
    {
        // A pointer back to the main page is required to display status messages.
        private MainPage rootPage = MainPage.Current;

        // The background task registration for the background advertisement watcher
        private IBackgroundTaskRegistration taskRegistration;
        // The watcher trigger used to configure the background task registration
        private BluetoothLEAdvertisementWatcherTrigger trigger;
        // A name is given to the task in order for it to be identifiable across context.
        private string taskName = "Beacon_BackgroundTask";
        // Entry point for the background task.
        private string taskEntryPoint = "BackgroundTasks.AdvertisementWatcherTask";
        // Local Name of the peripheral device, used to get permission to the device
        private string peripheralDeviceName = "LPF2 Smart Hub 2 I/O";
        // A service GUID on the peripheral device, used in the background watcher to find it
        private Guid peripheralDeviceServiceGUID = new Guid("00001523-1212-efde-1523-785feabcd123");

        public Scenario1_BeaconProximity()
        {
            this.InitializeComponent();
            this.initBluetoothAdvertisementWatcherTrigger();
        }

        private void initBluetoothAdvertisementWatcherTrigger()
        {
            this.trigger = new BluetoothLEAdvertisementWatcherTrigger();

            // Background advertisements require 1 advertisement filter. Here we filter by Guid in the Advertisement
            trigger.AdvertisementFilter.Advertisement.ServiceUuids.Add(peripheralDeviceServiceGUID);
            
            // Only activate the watcher when we're recieving values >= -80
            trigger.SignalStrengthFilter.InRangeThresholdInDBm = -80;

            // Stop watching if the value drops below -90 (user walked away)
            trigger.SignalStrengthFilter.OutOfRangeThresholdInDBm = -90;

            // Wait 5 seconds to make sure the device is really out of range
            trigger.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(5000);
            trigger.SignalStrengthFilter.SamplingInterval = TimeSpan.FromMilliseconds(2000);
        }

        /// <summary>
        /// Handle background task completion.
        /// </summary>
        /// <param name="task">The task that is reporting completion.</param>
        /// <param name="e">Arguments of the completion report.</param>
        private async void OnBackgroundTaskCompleted(BackgroundTaskRegistration task, BackgroundTaskCompletedEventArgs eventArgs)
        {
            // We get the advertisement(s) processed by the background task
            if (ApplicationData.Current.LocalSettings.Values.Keys.Contains(taskName))
            {
                string backgroundMessage = (string)ApplicationData.Current.LocalSettings.Values[taskName];
                // Serialize UI update to the main UI thread
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    // Write the message to console
                    System.Diagnostics.Debug.WriteLine(backgroundMessage);
                });
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            rootPage = MainPage.Current;

            // Get the existing task if already registered
            if (taskRegistration == null)
            {
                // Find the task if we previously registered it
                foreach (var task in BackgroundTaskRegistration.AllTasks.Values)
                {
                    if (task.Name == taskName)
                    {
                        taskRegistration = task;
                        taskRegistration.Completed += OnBackgroundTaskCompleted;
                        break;
                    }
                }
            }
            else
            {
                taskRegistration.Completed += OnBackgroundTaskCompleted;
            }

            // Attach handlers for suspension to stop the watcher when the App is suspended.
            App.Current.Suspending += App_Suspending;
            App.Current.Resuming += App_Resuming;

            rootPage.NotifyUser("Press Run to register watcher.", NotifyType.StatusMessage);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // Remove local suspension handlers from the App since this page is no longer active.
            App.Current.Suspending -= App_Suspending;
            App.Current.Resuming -= App_Resuming;

            // Since the watcher is registered in the background, the background task will be triggered when the App is closed 
            // or in the background. To unregister the task, press the Stop button.
            if (taskRegistration != null)
            {
                // Always unregister the handlers to release the resources to prevent leaks.
                taskRegistration.Completed -= OnBackgroundTaskCompleted;
            }
        }

        /// <summary>
        /// Invoked when application execution is being resumed.
        /// </summary>
        /// <param name="sender">The source of the resume request.</param>
        /// <param name="e"></param>
        private void App_Resuming(object sender, object e)
        {
            // Get the existing task if already registered
            if (taskRegistration == null)
            {
                // Find the task if we previously registered it
                foreach (var task in BackgroundTaskRegistration.AllTasks.Values)
                {
                    if (task.Name == taskName)
                    {
                        taskRegistration = task;
                        taskRegistration.Completed += OnBackgroundTaskCompleted;
                        break;
                    }
                }
            }
            else
            {
                taskRegistration.Completed += OnBackgroundTaskCompleted;
            }
        }

        /// <summary>
        /// Invoked when application execution is being suspended.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e"></param>
        void App_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            if (taskRegistration != null)
            {
                // Always unregister the handlers to release the resources to prevent leaks.
                taskRegistration.Completed -= OnBackgroundTaskCompleted;
            }
            rootPage.NotifyUser("App suspending.", NotifyType.StatusMessage);
        }

        /// <summary>
        /// When the user presses the request button, ask for permission to use the given device. This
        /// may take 30 seconds.
        /// </summary>
        /// <param name="sender">Instance that triggered the event.</param>
        /// <param name="e">Event data describing the conditions that led to the event.</param>
        private async void RequestButton_Click(object sender, RoutedEventArgs e)
        {
            rootPage.NotifyUser("Getting permission to device..", NotifyType.StatusMessage);

            // Find the device
            string deviceSelector = BluetoothLEDevice.GetDeviceSelectorFromDeviceName(this.peripheralDeviceName);
            var devices = await DeviceInformation.FindAllAsync(deviceSelector);

            // Return if we don't find a device - this will happen if the device we are searching for is not paired
            if (devices.Count == 0)
            {
                rootPage.NotifyUser("Device not found. Make sure it is paired.", NotifyType.ErrorMessage);
                return;
            }

            // Try to access the device to trigger the permissions dialogue. Immediately dispose the device so it is not
            // connected to us and we can view advertisements from it in the background.
            DeviceInformation firstDevice = devices[0];
            BluetoothLEDevice device = await BluetoothLEDevice.FromIdAsync(firstDevice.Id);
            device.Dispose();

            rootPage.NotifyUser("Received permission to device.", NotifyType.StatusMessage);
        }

        /// <summary>
        /// When the user presses the stop button, unregister the background task
        /// </summary>
        /// <param name="sender">Instance that triggered the event.</param>
        /// <param name="e">Event data describing the conditions that led to the event.</param>
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // Unregistering the background task will stop scanning if this is the only client requesting scan
            // First get the existing tasks to see if we already registered for it
            if (taskRegistration != null)
            {
                taskRegistration.Unregister(true);
                taskRegistration = null;
                rootPage.NotifyUser("Background watcher unregistered.", NotifyType.StatusMessage);
            }
            else
            {
                // At this point we assume we haven't found any existing tasks matching the one we want to unregister
                rootPage.NotifyUser("No registered background watcher found.", NotifyType.StatusMessage);
            }
        }

        /// <summary>
        /// When the user presses the run button, start the background watcher or alert the user that one is already registered
        /// </summary>
        /// <param name="sender">Instance that triggered the event.</param>
        /// <param name="e">Event data describing the conditions that led to the event.</param>
        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            // Registering a background trigger if it is not already registered. It will start background scanning.
            // First get the existing tasks to see if we already registered for it
            if (taskRegistration != null)
            {
                rootPage.NotifyUser("Background watcher already registered.", NotifyType.StatusMessage);
                return;
            }
            else
            {
                rootPage.NotifyUser("Registering background watcher.", NotifyType.StatusMessage);

                // Applications registering for background trigger must request for permission.
                BackgroundAccessStatus backgroundAccessStatus = await BackgroundExecutionManager.RequestAccessAsync();

                // Here, we do not fail the registration even if the access is not granted. Instead, we allow 
                // the trigger to be registered and when the access is granted for the Application at a later time,
                // the trigger will automatically start working again.

                // At this point we assume we haven't found any existing tasks matching the one we want to register
                // First, configure the task entry point, trigger and name
                var builder = new BackgroundTaskBuilder();
                builder.TaskEntryPoint = taskEntryPoint;
                builder.SetTrigger(trigger);
                builder.Name = taskName;

                // Now perform the registration. The registration can throw an exception if the current 
                // hardware does not support background advertisement offloading
                try
                {
                    taskRegistration = builder.Register();

                    // For this scenario, attach an event handler to display the result processed from the background task
                    taskRegistration.Completed += OnBackgroundTaskCompleted;

                    // Even though the trigger is registered successfully, it might be blocked. Notify the user if that is the case.
                    if ((backgroundAccessStatus == BackgroundAccessStatus.Denied) || (backgroundAccessStatus == BackgroundAccessStatus.Unspecified))
                    {
                        rootPage.NotifyUser("Not able to run in background. Application must given permission to be added to lock screen.",
                            NotifyType.ErrorMessage);
                    }
                    else
                    {
                        rootPage.NotifyUser("Background watcher registered.", NotifyType.StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    switch ((uint)ex.HResult)
                    {
                        case (0x80070032): // ERROR_NOT_SUPPORTED
                            rootPage.NotifyUser("The hardware does not support background advertisement offload.", NotifyType.ErrorMessage);
                            break;
                        default:
                            System.Diagnostics.Debug.WriteLine(ex.Message);                            
                            throw ex;
                    }
                }
            }
        }
    }
}
