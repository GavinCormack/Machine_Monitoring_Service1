using Newtonsoft.Json;
using Service1.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Timers;

namespace Service1
{
    public partial class Service1 : ServiceBase
    {
        #region Invoke Calls and .Net Processors
        // Needed to get the ticks for UpTime
        [DllImport( "kernel32" )]
        extern static UInt64 GetTickCount64();

        // Needed to show Service Status
        [DllImport( "advapi32.dll", SetLastError = true )]
        private static extern bool SetServiceStatus( IntPtr handle, ref ServiceStatus serviceStatus );

        public enum ServiceState
        {
            SERVICE_STOPPED = 0x00000001,
            SERVICE_START_PENDING = 0x00000002,
            SERVICE_STOP_PENDING = 0x00000003,
            SERVICE_RUNNING = 0x00000004,
            SERVICE_CONTINUE_PENDING = 0x00000005,
            SERVICE_PAUSE_PENDING = 0x00000006,
            SERVICE_PAUSED = 0x00000007,
        }

        [StructLayout( LayoutKind.Sequential )]
        public struct ServiceStatus
        {
            public long dwServiceType;
            public ServiceState dwCurrentState;
            public long dwControlsAccepted;
            public long dwWin32ExitCode;
            public long dwServiceSpecificExitCode;
            public long dwCheckPoint;
            public long dwWaitHint;
        };

        #endregion



        // GLOBAL VARIABLES
        ServiceStatus serviceStatus = new ServiceStatus();
        System.Timers.Timer timer;
        Machine machine = new Machine();
        Stats stats = new Stats();
        
        public Service1()
        {
            InitializeComponent();

            // Custom Event Log
            if(!EventLog.SourceExists( "<Service_Name> Logs" ))
            {
                EventLog.CreateEventSource( "<Service_Name> Logs", "<Service_Name> Monitoring" );
            }
            eventLog1.Source = "<Service_Name> Logs";
            eventLog1.Log = "<Service_Name> Monitoring";
        }


        private void stopTimer()
        {
            timer.Stop();
        }

        private void startTimer()
        {
            // Set up a Timer to Trigger every minute
            timer = new System.Timers.Timer();
            timer.Interval = 60000; // 60 Seconds
            timer.Elapsed += new ElapsedEventHandler( this.OnTimer );
            timer.Start();
        }


        private void OnTimer( object sender, ElapsedEventArgs args )
        {
            // Getting Machine Name
            machine.machineName = Environment.MachineName;
            eventLog1.WriteEntry( "Machine Name = " + machine.machineName );
           

            // Getting Machine IP
            IPHostEntry host = Dns.GetHostEntry( Dns.GetHostName() );
            foreach(IPAddress ip in host.AddressList)
            {
                if(ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    machine.machineIp = ip.ToString();
                    eventLog1.WriteEntry( "Machine IP = " + machine.machineIp );
                    break;
                }
            }


            // Getting Machine UpTime
            ulong ticks = GetTickCount64();
            TimeSpan upTimeSpan = TimeSpan.FromMilliseconds( ticks );
            machine.machineUpTime = String.Format( "{0:d}:{1:d}:{2:d}", upTimeSpan.Days, upTimeSpan.Hours, upTimeSpan.Minutes ); // DD:HH:MM
            eventLog1.WriteEntry( "Machine UpTime = " + machine.machineUpTime );


            // Getting Current Time
            DateTime currentTime = DateTime.Now.ToUniversalTime();
            stats.currentTime = currentTime;
            eventLog1.WriteEntry( "CurrentTime = " + stats.currentTime );

            // Getting CPU Percent
            PerformanceCounter cpuCounter = new PerformanceCounter( "Processor", "% Processor Time", "_Total" );
            cpuCounter.NextValue();
            Thread.Sleep( 500 );
            stats.cpuPercent = (float) Math.Round( cpuCounter.NextValue(), 2 );
            eventLog1.WriteEntry( "CPU Percent = " + stats.cpuPercent );


            // Getting RAM Percent
            ManagementObjectSearcher wmiObject = new ManagementObjectSearcher( "select * from Win32_OperatingSystem" );
            var memoryValues = wmiObject.Get().Cast<ManagementObject>().Select( mo => new
            {
                FreePhysicalMemory = Double.Parse( mo[ "FreePhysicalMemory" ].ToString() ),
                TotalVisibleMemorySize = Double.Parse( mo[ "TotalVisibleMemorySize" ].ToString() )
            } ).FirstOrDefault();

            if(memoryValues != null)
            {
                stats.ramPercent = (float) Math.Round( ( ( memoryValues.TotalVisibleMemorySize - memoryValues.FreePhysicalMemory ) / memoryValues.TotalVisibleMemorySize ) * 100, 2 );
                eventLog1.WriteEntry( "RAM Percent = " + stats.ramPercent );
            }


            // Getting Drives
            DriveInfo[] allDrives = DriveInfo.GetDrives();
            stats.machineDrives = new List<Drive>();
            foreach(DriveInfo d in allDrives)
            {
                if(d.DriveType == DriveType.Fixed)
                {
                    if(d.IsReady)
                    {
                        Drive drive = new Drive();
                        drive.driveName = d.Name;
                        eventLog1.WriteEntry( "Drive Name = " + drive.driveName );
                        drive.driveTotal = d.TotalSize;
                        eventLog1.WriteEntry( "Drive Total = " + drive.driveTotal );
                        drive.driveFree = d.TotalFreeSpace;
                        eventLog1.WriteEntry( "Drive Free = " + drive.driveFree );
                        drive.drivePercent = (float) Math.Round( ( (float) drive.driveFree / (float) drive.driveTotal ) * 100, 2 );
                        eventLog1.WriteEntry( "Drive % = " + drive.drivePercent );

                        stats.machineDrives.Add( drive );
                    }
                }
            }

            machine.machineStats = new List<Stats>();
            machine.machineStats.Add(stats);
            

            // Sending to the API
            string json = JsonConvert.SerializeObject( machine );
            eventLog1.WriteEntry( "JSON =  " + json, EventLogEntryType.Information );

            HttpWebRequest httpWebRequest = (HttpWebRequest) WebRequest.Create("http://13.74.255.194:43433/api/updateMachine");
            
            httpWebRequest.ContentType = "application/json";
            
            httpWebRequest.Method = "POST";

            StreamWriter streamWriter = new StreamWriter( httpWebRequest.GetRequestStream() );
            
            streamWriter.Write( json );
            streamWriter.Flush();
            streamWriter.Close();
            
            HttpWebResponse httpWebResponse = (HttpWebResponse) httpWebRequest.GetResponse();

            StreamReader streamReader = new StreamReader( httpWebResponse.GetResponseStream() );
            
            var result = streamReader.ReadToEnd();
            eventLog1.WriteEntry( "RESPONSE =  " + result, EventLogEntryType.Information );
            

            
            

        }

        
        
        #region Life Cycle Methods

        protected override void OnStart( string[] args )
        {
            eventLog1.WriteEntry( "In OnStart" );

            // Update the service state to Start Pending.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_START_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus( this.ServiceHandle, ref serviceStatus );


            startTimer();


            // Update the service state to Running.  
            serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
            SetServiceStatus( this.ServiceHandle, ref serviceStatus );
        }

        protected override void OnStop()
        {
            eventLog1.WriteEntry( "In OnStop" );

            // Update the service state to Stop Pending.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOP_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus( this.ServiceHandle, ref serviceStatus );


            stopTimer();


            // Update the service state to Stopped.  
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOPPED;
            SetServiceStatus( this.ServiceHandle, ref serviceStatus );
        }

        protected override void OnPause()
        {
            EventLog.WriteEntry( "In OnPause" );

            // Update the service state to Stop Pending.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_PAUSE_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus( this.ServiceHandle, ref serviceStatus );


            stopTimer();


            // Update the service state to Running.  
            serviceStatus.dwCurrentState = ServiceState.SERVICE_PAUSED;
            SetServiceStatus( this.ServiceHandle, ref serviceStatus );
        }
        protected override void OnContinue()
        {
            eventLog1.WriteEntry( "In OnContinue" );

            // Update the service state to Stop Pending.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_CONTINUE_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus( this.ServiceHandle, ref serviceStatus );


            startTimer();


            // Update the service state to Running.  
            serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
            SetServiceStatus( this.ServiceHandle, ref serviceStatus );
        }

        #endregion
    }
}
