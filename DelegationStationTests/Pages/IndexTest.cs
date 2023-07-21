using DelegationStation.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DelegationStationTests.Pages
{
    [TestClass]
    public class IndexTest : BunitTestContext
    {
        [TestMethod]
        public void RenderTest()
        {
            // Arrange
            //      Create fake services 
            
            //      Add Dependent Services




            // Act
            var cut = RenderComponent<NavMenu>();

            // Assert
            string html = @"";
        }
    }
}
