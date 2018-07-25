using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Service1.Models
{
    class Stats
    {
        public DateTime currentTime { get; set; }
        public float cpuPercent { get; set; }
        public float ramPercent { get; set; }
        public List<Drive> machineDrives { get; set; }
    }
}
