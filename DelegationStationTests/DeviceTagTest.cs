using DelegationStation.Services;
using DelegationStationShared.Models;

namespace DelegationStationTests
{
    [TestClass]
    public class TagsTests
    {       

        [TestMethod]
        public void PartitionKey_is_set_to_Id()
        {
            DeviceTag tag = new DeviceTag();
            Assert.AreEqual(tag.PartitionKey, typeof(DeviceTag).Name);
        }
    }
}