using System;
using System.Collections.Generic;

namespace Service1.Models
{
    class Machine
    {
        public string machineName { get; set; }
        public string machineIp { get; set; }
        public string machineUpTime { get; set; }
        public List<Stats> machineStats { get; set; }
    }
}
