using DelegationStation.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Extensions.Configuration;
using DelegationStation.Interfaces;
using DelegationStation.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace DelegationStation.Tests.Pages
{
    [TestClass]
    public class TagTests : BunitTestContext
    {
        [TestInitialize]
        public void RegisterPageState()
        {
            Services.AddScoped<PageStateService>();
        }

        [TestMethod]
        [DataRow(typeof(Tags), "input[placeholder='Tag Name']", "thead button")]
        [DataRow(typeof(SystemStatus), "#tag-search", "form")]
        public void TagSearchAndPaginationShouldPersistPerPageWithinSession(Type pageType, string searchSelector, string submitSelector)
        {
            Guid defaultId = Guid.NewGuid();
            var authContext = this.AddAuthorization();
            authContext.SetAuthorized("TEST USER");
            authContext.SetClaims(new System.Security.Claims.Claim("roles", defaultId.ToString()));

            var queries = new List<(int PageNumber, int PageSize, string? Name)>();
            var countSearches = new List<string?>();
            var deviceTags = new List<DeviceTag> { new DeviceTag { Name = "Finance" } };
            var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
            {
                GetDeviceTagCountAsyncIEnumerableOfStringString = (groupIds, name) =>
                {
                    countSearches.Add(name);
                    return Task.FromResult(45);
                },
                GetDeviceTagsByPageAsyncIEnumerableOfStringInt32Int32String = (groupIds, pageNumber, pageSize, name) =>
                {
                    queries.Add((pageNumber, pageSize, name));
                    return Task.FromResult(deviceTags);
                },
                GetDeviceCountByTagIdAsyncString = tagId => Task.FromResult(0)
            };
            var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
            {
                GetDevicesByTagAsyncString = tagId => Task.FromResult(new List<Device>())
            };
            var fakeCorpIdDBService = new DelegationStation.Interfaces.Fakes.StubICorpIdDBService()
            {
                GetCorpIDCounterAsync = () => Task.FromResult<CorpIDCounter?>(new CorpIDCounter(0))
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DefaultAdminGroupObjectId", defaultId.ToString() },
                { "MAX_CORPIDS_ALLOWED", "10000" }
            }).Build();

            Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
            Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
            Services.AddSingleton<ICorpIdDBService>(fakeCorpIdDBService);
            Services.AddSingleton<IConfiguration>(configuration);

            var firstPage = Render<DynamicComponent>(parameters => parameters.Add(p => p.Type, pageType));
            Assert.AreEqual((1, 10, string.Empty), queries.Last());
            firstPage.Find(searchSelector).Change("Finance");
            if (pageType == typeof(SystemStatus))
            {
                firstPage.Find(submitSelector).Submit();
            }
            else
            {
                firstPage.Find(submitSelector).Click();
            }
            Assert.AreEqual((1, 10, "Finance"), queries.Last());
            Assert.AreEqual("Finance", countSearches.Last());

            for (int page = 2; page <= 4; page++)
            {
                firstPage.Find(".pagination li:nth-child(4) a").Click();
                Assert.AreEqual((page, 10, "Finance"), queries.Last());
            }

            firstPage.Find(searchSelector).Change("Unsubmitted search");
            firstPage.Dispose();

            Type otherPageType = pageType == typeof(Tags) ? typeof(SystemStatus) : typeof(Tags);
            var otherPage = Render<DynamicComponent>(parameters => parameters.Add(p => p.Type, otherPageType));
            Assert.AreEqual((1, 10, string.Empty), queries.Last());
            Assert.AreEqual(string.Empty, countSearches.Last());
            otherPage.Dispose();

            var returnedPage = Render<DynamicComponent>(parameters => parameters.Add(p => p.Type, pageType));
            Assert.AreEqual("Finance", returnedPage.Find(searchSelector).GetAttribute("value"));
            Assert.AreEqual((4, 10, "Finance"), queries.Last());
            Assert.AreEqual("Finance", countSearches.Last());

            using var otherSession = new BunitContext();
            var otherAuthContext = otherSession.AddAuthorization();
            otherAuthContext.SetAuthorized("SECOND USER");
            otherAuthContext.SetClaims(new System.Security.Claims.Claim("roles", defaultId.ToString()));
            otherSession.Services.AddScoped<PageStateService>();
            otherSession.Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
            otherSession.Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
            otherSession.Services.AddSingleton<ICorpIdDBService>(fakeCorpIdDBService);
            otherSession.Services.AddSingleton<IConfiguration>(configuration);
            var otherUserPage = otherSession.Render<DynamicComponent>(parameters => parameters.Add(p => p.Type, pageType));
            Assert.AreEqual(string.Empty, otherUserPage.Find(searchSelector).GetAttribute("value") ?? string.Empty);
            Assert.AreEqual((1, 10, string.Empty), queries.Last());
            Assert.AreEqual(string.Empty, countSearches.Last());

            returnedPage.Find(".pagination li:nth-child(4) a").Click();
            Assert.AreEqual((5, 10, "Finance"), queries.Last());

            if (pageType == typeof(SystemStatus))
            {
                returnedPage.Find("form button.btn-secondary").Click();
            }
            else
            {
                returnedPage.Find(searchSelector).Change(string.Empty);
                returnedPage.Find(submitSelector).Click();
            }
            Assert.AreEqual((1, 10, string.Empty), queries.Last());
            returnedPage.Dispose();

            var clearedPage = Render<DynamicComponent>(parameters => parameters.Add(p => p.Type, pageType));
            Assert.AreEqual(string.Empty, clearedPage.Find(searchSelector).GetAttribute("value") ?? string.Empty);
            Assert.AreEqual((1, 10, string.Empty), queries.Last());
        }

        [TestMethod]
        [DataRow(typeof(Tags), 45, 4)]
        [DataRow(typeof(SystemStatus), 45, 4)]
        [DataRow(typeof(Tags), 25, 1)]
        [DataRow(typeof(SystemStatus), 25, 1)]
        [DataRow(typeof(Tags), 0, 1)]
        [DataRow(typeof(SystemStatus), 0, 1)]
        public void TagPagesShouldValidateSavedPageBeforeLoadingOnce(Type pageType, int totalTags, int expectedPage)
        {
            Guid defaultId = Guid.NewGuid();
            var authContext = this.AddAuthorization();
            authContext.SetAuthorized("TEST USER");
            authContext.SetClaims(new System.Security.Claims.Claim("roles", defaultId.ToString()));

            var requestedPages = new List<int>();
            int countQueries = 0;
            var deviceTags = totalTags == 0
                ? new List<DeviceTag>()
                : new List<DeviceTag> { new DeviceTag { Name = "Finance" } };
            var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
            {
                GetDeviceTagCountAsyncIEnumerableOfStringString = (groupIds, name) =>
                {
                    countQueries++;
                    return Task.FromResult(totalTags);
                },
                GetDeviceTagsByPageAsyncIEnumerableOfStringInt32Int32String = (groupIds, pageNumber, pageSize, name) =>
                {
                    requestedPages.Add(pageNumber);
                    return Task.FromResult(deviceTags);
                },
                GetDeviceCountByTagIdAsyncString = tagId => Task.FromResult(5)
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DefaultAdminGroupObjectId", defaultId.ToString() }
            }).Build();
            Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
            Services.AddSingleton<IDeviceDBService>(new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
            {
                GetDevicesByTagAsyncString = tagId => Task.FromResult(new List<Device>())
            });
            Services.AddSingleton<ICorpIdDBService>(new DelegationStation.Interfaces.Fakes.StubICorpIdDBService()
            {
                GetCorpIDCounterAsync = () => Task.FromResult<CorpIDCounter?>(new CorpIDCounter(0))
            });
            Services.AddSingleton<IConfiguration>(configuration);
            var sessionState = Services.GetRequiredService<PageStateService>();
            var savedState = pageType == typeof(Tags) ? sessionState.Tags : sessionState.SystemStatus;
            savedState.PageNumber = 4;
            savedState.SearchName = "Finance";

            var cut = Render<DynamicComponent>(parameters => parameters.Add(p => p.Type, pageType));

            CollectionAssert.AreEqual(new[] { expectedPage }, requestedPages);
            Assert.AreEqual(1, countQueries);
            Assert.AreEqual(expectedPage, savedState.PageNumber);
            Assert.IsFalse(cut.Markup.Contains("Error: retrieving tags."));
            if (totalTags > 0)
            {
                Assert.IsTrue(cut.Find("tbody").TextContent.Contains("Finance"));
                if (pageType == typeof(Tags))
                {
                    Assert.IsTrue(cut.Find("tbody").TextContent.Contains("5"));
                }
            }
        }

        [TestMethod]
        [DataRow("/Tags", "Tags")]
        [DataRow("/Tags/3", "Not found")]
        [DataRow("/SystemStatus", "SystemStatus")]
        public void TagPagesShouldUseUnnumberedRoutes(string path, string expectedPage)
        {
            Services.GetRequiredService<NavigationManager>().NavigateTo(path);

            var cut = Render<Router>(parameters => parameters
                .Add(p => p.AppAssembly, typeof(Tags).Assembly)
                .Add(p => p.Found, routeData => builder => builder.AddContent(0, routeData.PageType.Name))
                .Add(p => p.NotFound, builder => builder.AddContent(0, "Not found")));

            cut.MarkupMatches(expectedPage);
        }

        [TestMethod]
        public void TagsShouldRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                //      Create fake services
                List<DeviceTag> deviceTags = new List<DeviceTag>();
                DeviceTag deviceTag1 = new DeviceTag();
                deviceTag1.Name = "testName1";
                deviceTag1.Description = "testDescription1";
                deviceTags.Add(deviceTag1);
                DeviceTag deviceTag2 = new DeviceTag();
                deviceTag2.Name = "testName2";
                deviceTag2.Description = "testDescription2";
                deviceTags.Add(deviceTag2);
                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(deviceTags),
                    GetDeviceTagCountAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(2),
                    GetDeviceTagsByPageAsyncIEnumerableOfStringInt32Int32String =
                        (groupIds, pageNumber, pageSize, name) => Task.FromResult(deviceTags),
                    GetDeviceCountByTagIdAsyncString =
                        (tagId) => Task.FromResult(tagId == deviceTag1.Id.ToString() ? 5 : 7)
                };

                var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultId.ToString()},
                    {"Nested:Key1", "NestedValue1"},
                    {"Nested:Key2", "NestedValue2"}
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(myConfiguration)
                    .Build();


                //      Add Dependent Services
                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);


                // Act
                var cut = Render<Tags>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("testName1</td>"), $"Tag1 name should be rendered in the table. \\nActual:\\n{cut.Markup}\"");
                Assert.IsTrue(cut.Markup.Contains("testDescription1</td>"), "Tag1 description should be rendered in the table.");
                Assert.IsTrue(cut.Markup.Contains("testName2</td>"), "Tag2 name should be rendered in the table.");
                Assert.IsTrue(cut.Markup.Contains("testDescription2</td>"), "Tag2 description should be rendered in the table.");
                var row1 = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("testName1"));
                var row2 = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("testName2"));
                Assert.IsTrue(row1.TextContent.Contains("5"), $"Tag1 device count should be rendered in the table row.\nActual:\n{cut.Markup}");
                Assert.IsTrue(row2.TextContent.Contains("7"), $"Tag2 device count should be rendered in the table row.\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void TagsShouldRenderErrorForFailedDeviceCount()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                List<DeviceTag> deviceTags = new List<DeviceTag>();
                DeviceTag deviceTag1 = new DeviceTag();
                deviceTag1.Name = "testName1";
                deviceTag1.Description = "testDescription1";
                deviceTags.Add(deviceTag1);

                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(deviceTags),
                    GetDeviceTagCountAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(1),
                    GetDeviceTagsByPageAsyncIEnumerableOfStringInt32Int32String =
                        (groupIds, pageNumber, pageSize, name) => Task.FromResult(deviceTags),
                    GetDeviceCountByTagIdAsyncString =
                        (tagId) => throw new Exception("Simulated failure")
                };

                var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultId.ToString()}
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(myConfiguration)
                    .Build();

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act
                var cut = Render<Tags>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("Unable to retrieve device count"),
                    $"Error text should be rendered when device count retrieval fails.\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void UnauthorizedShouldNotRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetNotAuthorized();


                //      Create fake services
                List<DeviceTag> deviceTags = new List<DeviceTag>();
                DeviceTag deviceTag1 = new DeviceTag();
                deviceTag1.Name = "testName1";
                deviceTag1.Description = "testDescription1";
                deviceTags.Add(deviceTag1);
                DeviceTag deviceTag2 = new DeviceTag();
                deviceTag2.Name = "testName2";
                deviceTag2.Description = "testDescription2";
                deviceTags.Add(deviceTag2);
                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(deviceTags),
                    GetDeviceTagCountAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(2),
                    GetDeviceTagsByPageAsyncIEnumerableOfStringInt32Int32String =
                        (groupIds, pageNumber, pageSize, name) => Task.FromResult(deviceTags)
                };

                var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultId.ToString()},
                    {"Nested:Key1", "NestedValue1"},
                    {"Nested:Key2", "NestedValue2"}
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(myConfiguration)
                    .Build();

                //      Add Dependent Services
                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act
                var cut = Render<Tags>();

                // Assert
                cut.MarkupMatches(@"
<h3>Tags</h3>
Not Authorized to create Tags
<p>Not Authorized</p>");
            }
        }
    }
}