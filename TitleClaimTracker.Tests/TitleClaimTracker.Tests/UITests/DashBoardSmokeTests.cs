using System;
using Xunit;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.Blazor;

namespace TitleClaimTracker.Tests.UITests
{
    public class DashboardSmokeTests : IDisposable
    {
        private readonly IWebDriver _driver;
        // CRITICAL: Update this port if necessary
        private const string _baseUrl = "https://localhost:7210/";

        public DashboardSmokeTests()
        {
            var options = new ChromeOptions();
            options.AddArgument("--ignore-certificate-errors");
            _driver = new ChromeDriver(options);
        }

        [Fact]
        public void Dashboard_ShouldLoad_AndShowTitle()
        {
            _driver.Navigate().GoToUrl(_baseUrl);

            // FIX: Asserting against the known ACTUAL title content
            Assert.Contains("Filing Dashboard", _driver.Title);
            Assert.Contains("TitleClaimTracker", _driver.Title);
        }

        [Fact]
        public void CreateButton_ShouldNavigate_ToCreatePage()
        {
            _driver.Navigate().GoToUrl(_baseUrl);

            // FIX: Use By.Id locator which is more stable than By.LinkText
            var createButton = _driver.FindElement(By.Id("btn-create-filing"));
            createButton.Click();

            // Verification
            Assert.Contains("/Filings/Create", _driver.Url);
            var header = _driver.FindElement(By.TagName("h2"));
            Assert.Equal("Create Filing", header.Text);
        }

        public void Dispose()
        {
            _driver.Quit();
            _driver.Dispose();
        }
    }
}