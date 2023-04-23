using DelegationStation.Services;

namespace DelegationStationTests
{
    [TestClass]
    public class TagsTests
    {       

        [TestMethod]
        public void PartitionKey_is_set_to_type()
        {
            DeviceTag tag = new DeviceTag();
            Assert.AreEqual(tag.PartitionKey, tag.Type);

            new Mock
        }
    }
}