﻿// PRODUCTION
#if !(TESTNET || TESTNETDEV)
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NiceHashMiner.Devices;
using NiceHashMiner.Miners;
using NiceHashMiner.Switching;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using NiceHashMinerLegacy.Common.Enums;
using WebSocketSharp;
using NiceHashMiner.Stats.Models;

namespace NiceHashMiner.Stats
{
    internal static partial class NiceHashStats
    {
        private const int DeviceUpdateInterval = 60 * 1000;

        // Event handlers for socket
        public static event EventHandler OnBalanceUpdate;
        public static event EventHandler OnVersionUpdate;
        public static event EventHandler OnConnectionEstablished;
        public static event EventHandler<SocketEventArgs> OnVersionBurn;
        
        private static System.Threading.Timer _deviceUpdateTimer;

        public static void StartConnection(string address)
        {
            if (_socket == null)
            {
                _socket = new NiceHashSocket(address);
                _socket.OnConnectionEstablished += SocketOnOnConnectionEstablished;
                _socket.OnDataReceived += SocketOnOnDataReceived;
                _socket.OnConnectionLost += SocketOnOnConnectionLost;
            }
            _socket.StartConnection();
            _deviceUpdateTimer = new System.Threading.Timer(DeviceStatus_Tick, null, DeviceUpdateInterval, DeviceUpdateInterval);
        }

#region Socket Callbacks

        private static void SocketOnOnDataReceived(object sender, MessageEventArgs e)
        {
            try
            {
                if (e.IsText)
                {
                    Helpers.ConsolePrint("SOCKET", "Received: " + e.Data);
                    dynamic message = JsonConvert.DeserializeObject(e.Data);
                    switch (message.method.Value)
                    {
                        case "sma":
                            {
                                // Try in case stable is not sent, we still get updated paying rates
                                try
                                {
                                    var stable = JsonConvert.DeserializeObject(message.stable.Value);
                                    SetStableAlgorithms(stable);
                                } catch
                                { }
                                SetAlgorithmRates(message.data);
                                break;
                            }

                        case "balance":
                            SetBalance(message.value.Value);
                            break;
                        case "versions":
                            SetVersion(message.legacy.Value);
                            break;
                        case "burn":
                            OnVersionBurn?.Invoke(null, new SocketEventArgs(message.message.Value));
                            break;
                        case "exchange_rates":
                            SetExchangeRates(message.data.Value);
                            break;
                    }
                }
            } catch (Exception er)
            {
                NiceHashMinerLegacy.Common.Logger.Error("SOCKET", er.ToString());
                Helpers.ConsolePrint("SOCKET", er.ToString());
            }
        }

        private static void SocketOnOnConnectionEstablished(object sender, EventArgs e)
        {
            DeviceStatus_Tick(null); // Send device to populate rig stats

            OnConnectionEstablished?.Invoke(null, EventArgs.Empty);
        }

        #endregion

        #region Incoming socket calls

        private static void SetVersion(string version)
        {
            Version = version;
            OnVersionUpdate?.Invoke(null, EventArgs.Empty);
        }

        #endregion

        #region Outgoing socket calls

        public static void SetCredentials(string btc, string worker)
        {
            var data = new NicehashCredentials
            {
                btc = btc,
                worker = worker
            };
            if (BitcoinAddress.ValidateBitcoinAddress(data.btc) && BitcoinAddress.ValidateWorkerName(worker))
            {
                var sendData = JsonConvert.SerializeObject(data);

                // Send as task since SetCredentials is called from UI threads
                Task.Factory.StartNew(() => _socket?.SendData(sendData));
            }
        }

        private static void DeviceStatus_Tick(object state)
        {
            var devices = AvailableDevices.Devices;
            var deviceList = new List<JArray>();
            var activeIDs = MinersManager.GetActiveMinersIndexes();
            foreach (var device in devices)
            {
                try
                {
                    var array = new JArray
                    {
                        device.Index,
                        device.Name
                    };
                    var status = Convert.ToInt32(activeIDs.Contains(device.Index)) + ((int) device.DeviceType + 1) * 2;
                    array.Add(status);
                    array.Add((int) Math.Round(device.Load));
                    array.Add((int) Math.Round(device.Temp));
                    array.Add(device.FanSpeed);

                    deviceList.Add(array);
                }
                catch (Exception e) {
                    NiceHashMinerLegacy.Common.Logger.Error("SOCKET", e.ToString());
                    Helpers.ConsolePrint("SOCKET", e.ToString());
                }
            }
            var data = new DeviceStatusMessage
            {
                devices = deviceList
            };
            var sendData = JsonConvert.SerializeObject(data);
            // This function is run every minute and sends data every run which has two auxiliary effects
            // Keeps connection alive and attempts reconnection if internet was dropped
            _socket?.SendData(sendData);
        }

        #endregion


        public static void StateChanged()
        {
            // STUB FROM TESTNET
        }
    }
}
#endif