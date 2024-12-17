using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemoveCaseSensitiveDuplicates.Models
{
    public class Duplicate
    {
        public string Make { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string Tag0 { get; set; }
        public int Count { get; set; }

        public Duplicate()
        {
            Make = "";
            Model = "";
            SerialNumber = "";
            Tag0 = "";
            Count = 0;
        }
        public Duplicate (string make, string model, string serialNumber, string tag, int count)
        {
            Make = make;
            Model = model;
            SerialNumber = serialNumber;
            Tag0 = tag;
            Count = count;
        }


    }
}
