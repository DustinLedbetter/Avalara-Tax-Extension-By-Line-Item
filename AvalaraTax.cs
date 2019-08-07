/***************************************************************************************************************************************************
*                                                 GOD First                                                                                        *
* Author: Dustin Ledbetter                                                                                                                         *
* Release Date: 10-23-2018                                                                                                                         *
* Last Edited:  6-5-2019                                                                                                                           *
* Version: 11.0                                                                                                                                    *
* Purpose: This is an extension for pageflex storefronts that pulls the user's shipping info and order totals from the shipping page of the store, *
*          sends this information out to Avalara to calculate the orders taxes due amount, and returns it to the storefront for the user's order.  *
***************************************************************************************************************************************************/

/*
    References: There are five dlls referenced by this template:
    First three are added references
    1. PageflexServices.dll
    2. StorefrontExtension.dll
    3. SXI.dll
    Last two are part of our USING Avalara.AvaTax.RestClient (These are added From NuGet Package Management)
    4. Avalara.AvaTax.RestClient.net45.dll (not sure if still needed now that we are building our own call 6/3/2019)
    5. Newtonsoft.Json.9.0.1
    Also added RestSharp through NuGet management
    1. RestSharp
*/


using Newtonsoft.Json.Linq;
using Pageflex.Interfaces.Storefront;
using PageflexServices;
using RestSharp;
using System;
using System.Data.SqlClient;
using System.Text;
using System.Web;


namespace AvalaraTaxExtension
{

    public class AvalaraTax : SXIExtension
    {

        // Setup fields for use in the extension
        #region |--Fields--|
        // This section holds variables for code used throughout the program for quick refactoring as needed

        // Used to setup the minimum required fields
        private const string _UNIQUE_NAME = @"Avalara.Tax.Extension";
        private const string _DISPLAY_NAME = @"Services: Avalara Tax Extension";

        // Used to get the DB logon information
        private const string _DATA_SOURCE = @"ATDataSource";
        private const string _INITIAL_CATALOG = @"ATInitialCatalog";
        private const string _USER_ID = @"ATUserID";
        private const string _USER_PASSWORD = @"ATUserPassword";

        // Used to get the login info for the Avalara Site
        private const string _AV_ENVIRONMENT = @"AVEnvironment";
        private const string _AV_COMPANY_CODE = @"AVCompanyCode";
        private const string _AV_CUSTOMER_CODE = @"AVCustomerCode";
        private const string _AV_USER_ID = @"AVUserID";
        private const string _AV_USER_PASSWORD = @"AVUserPassword";

        // Used to setup if in debug mode and the logging path for if we are 
        private const string _AT_DEBUGGING_MODE = @"ATDebuggingMode";
        private const string _AT_DEBUGGING_MODE2 = @"ATDebuggingMode2";
        private static readonly string LOG_PART_1 = @"D:\Pageflex\Deployments\";
        private static readonly string LOG_PART_2 = "Logs";
        private static readonly string LOG_PART_3 = "Avalara_Tax_Extension_Logs";
        private static readonly string LOG_PART_4 = "Avalara_Extension_Log_File_";
        private static readonly string _SA_SITE_TYPE = @"SaSiteType";

        // Create instance for using the LogMessageToFile class methods
        LogMessageToFile LMTF = new LogMessageToFile();

        // Variables to hold our totals from the DB retrieval step
        decimal subTotal = 0;
        decimal shippingCharge = 0;
        decimal handlingCharge = 0;
        decimal totalTaxableAmount = 0;

        // Variable to send to error emails
        public static string ErrorOrderID;

        // Used to hold the returned tax from Avalara
        double tax = 0;

        #endregion


        // Setup main properties and logging
        #region |--Properties and Logging--|
        // At a minimum your extension must override the DisplayName and UniqueName properties.


        // The UniqueName is used to associate a module with any data that it provides to Storefront.
        public override string UniqueName
        {
            get
            {
                return _UNIQUE_NAME;
            }
        }

        // The DisplayName will be shown on the Extensions and Site Options pages of the Administrator site as the name of your module.
        public override string DisplayName
        {
            get
            {
                return _DISPLAY_NAME;
            }
        }

        // Gets the parameters entered on the extension page for this extension
        protected override string[] PARAMS_WE_WANT
        {
            get
            {
                return new string[12]
                {
                    _AT_DEBUGGING_MODE,
                    _AT_DEBUGGING_MODE2,
                    _DATA_SOURCE,
                    _INITIAL_CATALOG,
                    _USER_ID,
                    _USER_PASSWORD,
                    _AV_COMPANY_CODE,
                    _AV_CUSTOMER_CODE,
                    _AV_ENVIRONMENT,
                    _AV_USER_ID,
                    _AV_USER_PASSWORD,
                    _SA_SITE_TYPE
                };
            }
        }

        // Used to access the storefront to retrieve variables
        ISINI SF { get { return Storefront; } }

        #endregion


        // Config Page Inside Admin Side of storefront where Developer presets values 
        #region |--This section setups up the extension config page on the storefront to takes input for variables from the user at setup to be used in our extension--|

        // This section sets up on the extension page on the storefront a check box for users to turn on or off debug mode and text fields to get logon info for DB and Avalara
        public override int GetConfigurationHtml(KeyValuePair[] parameters, out string HTML_configString)
        {
            // Load and check if we already have a parameter set
            LoadModuleDataFromParams(parameters);

            // If not then we setup one 
            if (parameters == null)
            {
                SConfigHTMLBuilder sconfigHtmlBuilder = new SConfigHTMLBuilder();
                sconfigHtmlBuilder.AddHeader();

                // Add checkbox to let user turn on and off debug mode
                sconfigHtmlBuilder.AddServicesHeader("Debug Mode:", "");
                sconfigHtmlBuilder.AddCheckboxField("Debugging Information", _AT_DEBUGGING_MODE, "true", "false", (string)ModuleData[_AT_DEBUGGING_MODE] == "true");
                sconfigHtmlBuilder.AddTip(@"This box should be checked if you wish for debugging information to be output to the Storefront's Logs Page. <br> &nbsp&nbsp&nbsp&nbsp&nbsp&nbsp 
                                            Whether this box is checked or not, the extension will log to a .txt file saved to the site's deployment folder.");
                sconfigHtmlBuilder.AddTip(@"* Make sure the 'Logs/Avalara_Tax_Extension_Logs' folders have been created to hold the .txt files as the extension will crash without it *");
                sconfigHtmlBuilder.AddCheckboxField("Standard Running Logs", _AT_DEBUGGING_MODE2, "true", "false", (string)ModuleData[_AT_DEBUGGING_MODE2] == "true");
                sconfigHtmlBuilder.AddTip(@"Unchecking this box will remove the 4 storefront messages that are logged regardless of whether debug mode is on or not to ensure that the extension is running");
                sconfigHtmlBuilder.AddTextField("Site Type", _SA_SITE_TYPE, (string)ModuleData[_SA_SITE_TYPE], true, true, "");
                sconfigHtmlBuilder.AddTip(@"This field should contain 'Live' or 'Dev' to help when sending error messages out and for logging.");

                // Add textboxes to get the DB login info
                sconfigHtmlBuilder.AddServicesHeader("DataBase Logon Info:", "");
                sconfigHtmlBuilder.AddTextField("DataBase Data Source", _DATA_SOURCE, (string)ModuleData[_DATA_SOURCE], true, true, "");
                sconfigHtmlBuilder.AddTip(@"This field should contain the path that the deployment can be found with. <br> &nbsp&nbsp&nbsp&nbsp&nbsp&nbsp Example for a site deployed on dev: 172.18.0.67\pageflexdev");
                sconfigHtmlBuilder.AddTextField("DataBase Initial Catalog", _INITIAL_CATALOG, (string)ModuleData[_INITIAL_CATALOG], true, true, "");
                sconfigHtmlBuilder.AddTip(@"The field should contain the name used to reference this storefront's Database. <br> &nbsp&nbsp&nbsp&nbsp&nbsp&nbsp Example for a site deployed on dev: Interface");
                sconfigHtmlBuilder.AddTextField("DataBase User ID", _USER_ID, (string)ModuleData[_USER_ID], true, true, "");
                sconfigHtmlBuilder.AddTip(@"This field should contain the User ID for logging into the storefront's database");
                sconfigHtmlBuilder.AddPasswordField("DataBase User Password", _USER_PASSWORD, (string)ModuleData[_USER_PASSWORD], true, true, "");
                sconfigHtmlBuilder.AddTip(@"This field should contain the Password for logging into the storefront's database");

                // Add textboxes to get the Avalara login info
                sconfigHtmlBuilder.AddServicesHeader("Avalara Logon Info:", "");
                sconfigHtmlBuilder.AddTextField("Avalara Environment", _AV_ENVIRONMENT, (string)ModuleData[_AV_ENVIRONMENT], true, true, "");
                sconfigHtmlBuilder.AddTip(@"The field should contain the environment used on the Avalara Site <br> &nbsp&nbsp&nbsp&nbsp&nbsp&nbsp Types: 'Production' or 'Sandbox'");
                sconfigHtmlBuilder.AddTextField("Avalara Company Code", _AV_COMPANY_CODE, (string)ModuleData[_AV_COMPANY_CODE], true, true, "");
                sconfigHtmlBuilder.AddTip(@"The field should contain the company code used on the Avalara Site");
                sconfigHtmlBuilder.AddTextField("Avalara Customer Code", _AV_CUSTOMER_CODE, (string)ModuleData[_AV_CUSTOMER_CODE], true, true, "");
                // Added spacing is to ensure that the textboxes for this and DB logon section are aligned
                sconfigHtmlBuilder.AddTip(@"This field should contain the customer code on the Avalara Site&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp");
                sconfigHtmlBuilder.AddTextField("Avalara User ID", _AV_USER_ID, (string)ModuleData[_AV_USER_ID], true, true, "");
                sconfigHtmlBuilder.AddTip(@"This field should contain the User ID for logging into the Avalara Site");
                sconfigHtmlBuilder.AddPasswordField("Avalara Password", _AV_USER_PASSWORD, (string)ModuleData[_AV_USER_PASSWORD], true, true, "");
                sconfigHtmlBuilder.AddTip(@"This field should contain the Password for logging into the Avalara Site");

                // Footer info and set to configstring
                sconfigHtmlBuilder.AddServicesFooter();
                HTML_configString = sconfigHtmlBuilder.html;
            }
            // If we do then move along
            else
            {
                SaveModuleData();
                HTML_configString = null;
            }
            return 0;
        }

        #endregion


        // Unused
        #region |--This section is used to determine if we are in the "shipping" or "payment" module on the storefront or not--|

        // Unused as taxes are calculated in several places on the storefront

        /*
        public override bool IsModuleType(string x)
        {
            // If we are in the shipping module return true to begin processes for this module
            if (x == "Shipping")
            {
                return true;
            }
            // If there is no shipping step and we go straight to the payment module return true to begin processes for this module here
            else if (x == "Payment")
            {
                return true;
            }
            // if we are not in either then just keep waiting
            else
                return false;
        }
        */

        #endregion


        // This is where main method is for calculating taxes
        #region |--This section is used to figure out the tax rates and get the zipcode entered on the shipping form--|

        // This method is used to get adjust the tax rate for the user's order
        public override int CalculateTax2(string OrderID, double taxableAmount, string currencyCode, string[] priceCategories, string[] priceTaxLocales, double[] priceAmount, string[] taxLocaleId, ref double[] taxAmount)
        {


            // Retrieve storefrontname and type to use with logging
            string storeFrontName = SF.GetValue(FieldType.SystemProperty, SystemProperty.STOREFRONT_NAME, null);
            string storefrontType = Storefront.GetValue("ModuleField", _SA_SITE_TYPE, _UNIQUE_NAME);


            // Used to see what the full url is currently for debugging
            //LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"full url: " + HttpContext.Current.Request.Url.AbsolutePath.ToLower());
            //if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true") { LogMessage($"full url: " + HttpContext.Current.Request.Url.AbsolutePath.ToLower()); }

            // Check to make sure we are not calling the method on the decline/reject order page as it fails on the db call 
            // Check added 2/27/2019 Per Mike Lowell and Justin Heath and Dustin Ledbetter to ensure storefront does not crash when orders are rejected 
            if (HttpContext.Current.Request.Url.AbsolutePath.ToLower().Contains("usercontentapprovalsreview") == true)
            {
                // IF section to ensure that we don't calculate taxes when an order is rejected
                #region |--This Section is used to make sure we do not try to calcualte taxes when an order is rejected which fails during the db call we currently use for calculating taxes--|

                // Log to ensure that it is known that we have bypassed the calculations done here
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Taxes were not calculated because we are on the order approval page and they must be done from Backside");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Calculating here throws errors due to database connection timing out");

                // Check if debug mode 2 is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE2] == "true")
                {
                    // Log to ensure that it is known that we have bypassed the calculations done here  
                    LogMessage($"Taxes were not calculated because we are on the order approval page and they must be done from Backside");
                    LogMessage($"Calculating here throws errors due to database connection timing out");
                }

                // Return sucess to ensure method is completed 
                return eSuccess;

                #endregion

            }
            else
            {


                // Set variable to use if error occurs
                ErrorOrderID = OrderID;


                // Show what method was passed when called
                #region |--This section of code shows what we have been passed if debug mode is "on"--|

                // Check if debug mode 2 is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE2] == "true")
                {
                    // Log that the extension is being called  
                    LogMessage($"Starting Avalara Tax Extension Process for order: {OrderID}");
                }

                // These Log the messages to a log .txt file
                // The logs an be found in the Logs folder in the storefront's deployment
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*                        *");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*          START         *");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*                        *");

                // Shows what values are passed at beginning in .txt file
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"OrderID is:              {OrderID}");                              // Tells the id for the order being calculated
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"TaxableAmount is:        {taxableAmount.ToString()}");             // Tells the amount to be taxed (currently set to 0)
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"CurrencyCode is:         {currencyCode}");                         // Tells the type of currency used
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"PriceCategories is:      {priceCategories.Length.ToString()}");    // Not Null, but empty
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"PriceTaxLocales is:      {priceTaxLocales.Length.ToString()}");    // Not Null, but empty
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"PriceAmount is:          {priceAmount.Length.ToString()}");        // Not Null, but empty
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"TaxLocaleId is:          {taxLocaleId.Length.ToString()}");        // Shows the number of tax locales found for this order

                // Removed 2/20/2019 as causes error when this method is called during an order rejection step
                //LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"TaxLocaleId[0] is:       {taxLocaleId[0].ToString()}");            

                // Check if debug mode is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                {
                    // Used to help to see where these mesages are in the Storefront logs page
                    LogMessage($"*                        *");       // Adds a space for easier viewing
                    LogMessage($"*          START         *");       // Show when we start this process
                    LogMessage($"*                        *");

                    // Shows what values are passed at beginning
                    LogMessage($"OrderID is:              {OrderID}");                                    // Tells the id for the order being calculated
                    LogMessage($"TaxableAmount is:        {taxableAmount.ToString()}");                   // Tells the amount to be taxed
                    LogMessage($"CurrencyCode is:         {currencyCode}");                               // Tells the type of currency used
                    LogMessage($"PriceCategories is:      {priceCategories.Length.ToString()}");          // Not Null, but empty
                    LogMessage($"PriceTaxLocales is:      {priceTaxLocales.Length.ToString()}");          // Not Null, but empty
                    LogMessage($"PriceAmount is:          {priceAmount.Length.ToString()}");              // Not Null, but empty
                    LogMessage($"TaxLocaleId is:          {taxLocaleId.Length.ToString()}");              // Shows the number of tax locales found for this order

                    // Removed 2/20/2019 as causes error when this method is called during an order rejection step
                    //LogMessage($"TaxLocaleId[0] is:       {taxLocaleId[0].ToString()}");                 
                }

                #endregion


                // Show what user entered on shipping page (address information)
                #region |--This section is where we get and set the values from the shipping page where the user has entered their address info--|

                // Shows the section where we get and display what has been added to the shipping page fields in the .txt file
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*                        *");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*Shipping Fields Section *");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*                        *");

                // Check if debug mode is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                {
                    // Shows the section where we get and display what has been added to the shipping page fields
                    LogMessage($"*                        *");
                    LogMessage($"*Shipping Fields Section *");
                    LogMessage($"*                        *");
                }

                // This section saves the user's shipping info to variables to use with calculating the tax rate to return 
                // Listed in the same order as in the address book on the site
                var SFirstName = Storefront.GetValue("OrderField", "ShippingFirstName", OrderID);
                var SLastName = Storefront.GetValue("OrderField", "ShippingLastName", OrderID);
                var SAddress1 = Storefront.GetValue("OrderField", "ShippingAddress1", OrderID);
                var SAddress2 = Storefront.GetValue("OrderField", "ShippingAddress2", OrderID);
                var SCity = Storefront.GetValue("OrderField", "ShippingCity", OrderID);
                var SState = Storefront.GetValue("OrderField", "ShippingState", OrderID);
                var SPostalCode = Storefront.GetValue("OrderField", "ShippingPostalCode", OrderID);
                var SCountry = Storefront.GetValue("OrderField", "ShippingCountry", OrderID);
                var hCharge = Storefront.GetValue("OrderField", "HandlingCharge", OrderID);
                var sCharge = Storefront.GetValue("OrderField", "ShippingCharge", OrderID);

                // Log to show that we have retrieved the zipcode form the shipping page in the .txt file
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Shipping FirstName:      {SFirstName}");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Shipping LastName:       {SLastName}");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Shipping Address1:       {SAddress1}");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Shipping Address2:       {SAddress2}");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Shipping City:           {SCity}");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Shipping State:          {SState}");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Shipping PostalCode:     {SPostalCode}");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Shipping Country:        {SCountry}");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Handling Charge:         {hCharge}");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Shipping Charge:         {sCharge}");

                // Check if debug mode is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                {
                    // Log to show that we have retrieved the zipcode form the shipping page
                    LogMessage($"Shipping FirstName:      {SFirstName}");
                    LogMessage($"Shipping LastName:       {SLastName}");
                    LogMessage($"Shipping Address1:       {SAddress1}");
                    LogMessage($"Shipping Address2:       {SAddress2}");
                    LogMessage($"Shipping City:           {SCity}");
                    LogMessage($"Shipping State:          {SState}");
                    LogMessage($"Shipping PostalCode:     {SPostalCode}");
                    LogMessage($"Shipping Country:        {SCountry}");
                    LogMessage($"Handling Charge:         {hCharge}");
                    LogMessage($"Shipping Charge:         {sCharge}");
                }

                // Check if debug mode 2 is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE2] == "true")
                {
                    // Log that the extension has retrieved the user's shipping information
                    LogMessage($"Avalara Tax Extension has retrieved the user's shipping information");
                }

                #endregion


                // Database Connection and retrieval step
                #region |--Database call to retrieve the subtotal--|

                // Shows the section where we change the tax rate in the .txt file
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*                        *");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*Tax Rates Section Part 1*");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*                        *");

                // Set the tax amount based on a few zipcodes and send it back to pageflex in the .txt file
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Tax amount is:           " + taxAmount[0].ToString() + " before we make our DB or Avalara calls");

                // Check if debug mode is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                {
                    // Shows the section where we change the tax rate
                    LogMessage($"*                        *");
                    LogMessage($"*Tax Rates Section Part 1*");
                    LogMessage($"*                        *");

                    // Set the tax amount based on a few zipcodes and send it back to pageflex
                    LogMessage($"Tax amount is:           " + taxAmount[0].ToString() + " before we make our DB or Avalara calls");
                }

                // Get our DB logon info from the storefront
                string dataSource = Storefront.GetValue("ModuleField", _DATA_SOURCE, _UNIQUE_NAME);
                string initialCatalog = Storefront.GetValue("ModuleField", _INITIAL_CATALOG, _UNIQUE_NAME);
                string userID = Storefront.GetValue("ModuleField", _USER_ID, _UNIQUE_NAME);
                string userPassword = Storefront.GetValue("ModuleField", _USER_PASSWORD, _UNIQUE_NAME);
                //LogMessage($"User Password is:        {userPassword}");                                  

                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"DB logon information:");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Data Source is:          {dataSource}");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Initial Catalog is:      {initialCatalog}");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"User ID is:              {userID}");

                // Check if debug mode is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                {
                    // Log messages to show what was retrieved from the storefront 
                    LogMessage($"DB logon information:");
                    LogMessage($"Data Source is:          {dataSource}");
                    LogMessage($"Initial Catalog is:      {initialCatalog}");
                    LogMessage($"User ID is:              {userID}");
                    //LMTF.LogMessagesToFile(storeFrontName, LOG_FILENAME1, LOG_FILENAME2, $"User Password is:        {userPassword}");          
                }

                // Our credentials to connect to the DB
                string connectionString = $"Data Source={dataSource};" +
                                            $"Initial Catalog={initialCatalog};" +
                                            $"User ID={userID};" +
                                            $"Password={userPassword};" +
                                            "Persist Security Info=True;" +
                                            "Connection Timeout=15";


                // Unused
                #region |--Print out full connection string--|
                /*
                // Check if debug mode is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                {
                    LogMessage($"Full Connection String:  {connectionString}");                                                                
                    LMTF.LogMessagesToFile(storeFrontName, LOG_FILENAME1, LOG_FILENAME2, $"Full Connection String:  {connectionString}");       
                */
                #endregion


                // The query to be ran on the DB
                //
                // Note: 6/3/2019 By Dustin Ledbetter:
                //       This query retrieves the subtotal as we originally passed that value to Avalara, but it was decided to send the line items in the cart to Ava on a line by line basis instead of as a subtotal
                //       I chose to leave the query alone incase we should ever need to go back to other way.
                //
                /*
                    * Query:   SELECTS the "Order Group ID", the "Price"(Cast as money), 
                    *          the "Shipping Charge"(Cast as money) COALESCE(CAST(Shipments.ShippingAmount / 100.00 as money), 0.00) coalesce is used incase there is no shipping step or shipping is null,
                    *          and the "Handling Charge"(Cast as money)
                    *          FROM three tables: "OrderedDocuments", "OrderGroups", and "Shipments" that have been INNER JOINED together based on the "OrderGroupID"
                    *          WHERE the "OrderGroupID" equals the orderID of our current order on the storefront
                    */
                string queryString = "SELECT COALESCE(CAST(Shipments.ShippingAmount / 100.00 AS money), 0.00) AS Shipping, " +
                                            "CAST(OrderGroups.HandlingCharge / 100.00 AS money) AS Handling, " +
                                            "CAST(SUM(OrderedDocuments.Price) / 100.00 AS money) AS Price " +
                                        "FROM Shipments LEFT OUTER JOIN " +
                                            "OrderedDocuments ON Shipments.OrderGroupID = OrderedDocuments.OrderGroupID LEFT OUTER JOIN " +
                                            "OrderGroups ON Shipments.OrderGroupID = OrderGroups.OrderGroupID " +
                                        "WHERE(Shipments.OrderGroupID = @orderID) " +
                                        "GROUP BY Shipments.ShippingAmount, OrderGroups.HandlingCharge";


                // Used to get count and ensure we have data returned
                string queryString2 = "SELECT count(*), OrderedDocuments.OrderGroupID," +
                                            "CAST(OrderedDocuments.Price / 100.00 as money) as Price," +
                                            "COALESCE(CAST(Shipments.ShippingAmount / 100.00 as money), 0.00) as Shipping," +
                                            "CAST(OrderGroups.HandlingCharge / 100.00 as money) as Handling " +
                                        "FROM OrderedDocuments " +
                                            "INNER JOIN OrderGroups ON OrderGroups.OrderGroupID = OrderedDocuments.OrderGroupID " +
                                            "INNER JOIN Shipments ON Shipments.OrderGroupID = OrderedDocuments.OrderGroupID " +
                                        "WHERE OrderedDocuments.OrderGroupID = @orderID " +
                                        "GROUP BY OrderedDocuments.OrderGroupID, OrderedDocuments.Price, Shipments.ShippingAmount, OrderGroups.HandlingCharge";


                // This is how to call it using a stored procedure I currently have setup on "Interface DB"
                //string queryString = "USE [Interface] " + "DECLARE @return_value Int EXEC @return_value = [dbo].[GetRates] @OrderID = 1787 SELECT @return_value as 'Return Value'";

                // This logs that we are starting the DB connection process
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "Starting DB calls");
                if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true") LogMessage("Starting DB calls");

                // Safety net for db connection and calls
                try
                {

                    // Setup the connection to the db using our credentials
                    using (SqlConnection connection = new SqlConnection(connectionString))
                    {

                        // Log that we are starting DB connection to check count for returned data
                        LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "Starting DB connection to check count for returned data");
                        if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true") LogMessage("Starting DB connection to check count for returned data");

                        //Setup our query to call
                        SqlCommand counter = new SqlCommand(queryString2, connection);
                        counter.Parameters.AddWithValue("@orderID", OrderID);

                        // Get returned count of query call to ensure we actually get something
                        connection.Open();
                        int icount = (int)counter.ExecuteScalar();
                        connection.Close();

                        // Log that we have finished DB connection to check count for returned data
                        LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "Finished DB connection to check count for returned data");
                        if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true") LogMessage("Finished DB connection to check count for returned data");


                        // Good practice check to ensure we have data before trying to run extra code
                        if (icount > 0)
                        {
                            // Log that we have something greater than zero in count check
                            LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "Query count greater than zero");
                            LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "Query count: " + icount.ToString());

                            // Check if debug mode is turned on; If it is then we log these messages
                            if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                            {
                                // Log that we have something greater than zero in count check
                                LogMessage($"Query count greater than zero");
                                LogMessage($"Query count: " + icount.ToString());
                            }

                            // Log that we are starting DB connection to run main query for data
                            LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "Starting DB connection to run main query for data");
                            if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true") LogMessage("Starting DB connection to run main query for data");

                            // Setup our query to call
                            SqlCommand commandGetRates = new SqlCommand(queryString, connection);
                            commandGetRates.Parameters.AddWithValue("@orderID", OrderID);

                            // Open the connection using what we have setup
                            connection.Open();

                            // Log successful connection and query run
                            LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "Connection opened: Starting SQL Reader Block");
                            if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true") LogMessage("Connection opened: Starting SQL Reader Block");

                            // Setup a reader to handle what our query returns
                            using (SqlDataReader reader = commandGetRates.ExecuteReader())
                            {

                                // Used for testing
                                //LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "inside using");

                                // Read back what our query retrieves
                                while (reader.Read())
                                {

                                    // Used for testing
                                    //LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "inside while");

                                    // Log from reader what data we retrieved
                                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, string.Format("From DB Reader: OrderID: " + OrderID + " Price: {2}, Shipping: {0}, Handling: {1}", reader["Shipping"], reader["Handling"], reader["Price"]));
                                    if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                                    {
                                        // Log and show the results we retrieved from the DB
                                        LogMessage(string.Format("From DB Reader: OrderID: " + OrderID + " Price: {2}, Shipping: {0}, Handling: {1}", reader["Shipping"], reader["Handling"], reader["Price"]));
                                    }

                                    // Filling the variables to hold our totals from the DB retrieval step
                                    subTotal = Convert.ToDecimal(reader["Price"]); // This gets the subtotal from the database

                                    // Make subtotal += so it can handle multiple items from the shopping cart when an order is placed
                                    shippingCharge = Convert.ToDecimal(reader["Shipping"]); // This gets the shipping charge from the database
                                    handlingCharge = Convert.ToDecimal(reader["Handling"]); // This gets the handling charge from the database
                                }

                                // Calculate the total amount to be taxed by Avalara
                                // Edited 6/3/2019 by Dustin Ledbetter: removed the subtotal amount here as we will build the line items amounts further on
                                totalTaxableAmount = shippingCharge + handlingCharge;

                                // Log the amounts from variables
                                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Totals read from the DB and stored in variables (SubTotal not included in total taxable amount):");
                                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"SubTotal:                {subTotal}");
                                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"ShippingCharge:          {shippingCharge}");
                                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"HandlingCharge:          {handlingCharge}");                                                                                                                     // Log message to show what the total taxable amount that will be sent to Avalara is
                                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Total Taxable Amount:    {totalTaxableAmount}");

                                if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                                {
                                    // Log messages to show what we retrieved from DB and have stored into variables
                                    LogMessage($"Totals read from the DB and stored in variables (SubTotal not included in total taxable amount):");
                                    LogMessage($"SubTotal:                {subTotal}");
                                    LogMessage($"ShippingCharge:          {shippingCharge}");
                                    LogMessage($"HandlingCharge:          {handlingCharge}");

                                    LogMessage($"Total Taxable Amount:    {totalTaxableAmount}");
                                }

                                // Always call Close when done reading.
                                reader.Close();
                            }

                            // Called by dispose, but good practice to close when done with connection.
                            connection.Close();

                        }
                        else
                        {
                            // Log that we have something that is or less than zero in count check
                            LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "Query count is or less than than zero");
                            LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "Query count: " + icount.ToString());

                            // Check if debug mode is turned on; If it is then we log these messages
                            if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                            {
                                // Log that we have something that is or less than zero in count check
                                LogMessage($"Query count is or less than than zero");
                                LogMessage($"Query count: " + icount.ToString());
                            }

                            // return success to end our call
                            return eSuccess;
                        }

                    }   // End the using statement for connection

                }
                catch
                {
                    // Log issue with storefront and to file regardless of whether in debug mode or not
                    LogMessage("Error in DB connection and data retrieval process");     // This logs that there was an error in the DB process
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "Error in DB connection and data retrieval process");

                    // Get the storefront's name from storefront and Date and time stamps as desired
                    string sfName = SF.GetValue(FieldType.SystemProperty, SystemProperty.STOREFRONT_NAME, null);
                    string currentLogDate = DateTime.Now.ToString("MMddyyyy");
                    string currentLogTimeInsertMain = DateTime.Now.ToString("HH:mm:ss tt");

                    //Setup our date and time for error
                    string ErrorDate = string.Format("Date: {0}  Time: {1:G} <br>", currentLogDate, currentLogTimeInsertMain);

                    // Setup our email body and message
                    string subjectstring = "The " + storefrontType + " Storefront: \"" + sfName + "\" had an ERROR occur in the DB Connection Process";
                    string bodystring = "The " + storefrontType + " Storefront: \"" + sfName + "\" had an ERROR occur in the DB Connection Process <br>" +
                                        ErrorDate +
                                        "Extension: Avalara Tax Extension <br>" +
                                        "ERROR occured with Order ID: " + ErrorOrderID;

                    // Call method to send our error as an email to developers maintaining sites
                    EmailErrorNotify.CreateMessage(subjectstring, bodystring);

                    // Log issue with storefront and to file regardless of whether in debug mode or not
                    LogMessage($"Error in DB connection send email method called");
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Error in DB connection send email method called");

                    LogMessage($"Email sent successfully: {EmailErrorNotify.checkFlag}");
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Email sent successfully: {EmailErrorNotify.checkFlag}");
                }

                // Check if debug mode 2 is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE2] == "true")
                {
                    // Log that the extension has retrieved the rest of the information to calculate the new tax rate
                    LogMessage($"Avalara Tax Extension has successfully connected to DB and retrieved the amounts to calculate tax");
                }


                #endregion


                // Get the prices for every line item in user's cart to calculate taxes on
                // Edited 6/3/2019 by Dustin Ledbetter: This section was added to get the line item amounts to send to Avalara to have the taxes calculated on
                #region |--Storefront call to get array of line item prices for tax calculating--|

                // Get userID to access correct cart and setup arrays to hold the users cart items and their prices
                var usersID = Storefront.GetValue(FieldType.SystemProperty, SystemProperty.LOGGED_ON_USER_ID, null);
                // User's cart items are saved as an ID, we first get them and store them here in this array
                string[] shoppingCartContents = Storefront.GetListValue("UserListProperty", "DocumentsInShoppingCart", usersID);
                // We will use this array to hold the prices for each line item once they are retrieved below
                decimal[] shoppingCartPrices = new decimal[shoppingCartContents.Length];

                // Loop through our shoppingCartContents array of cart Item IDs and retrieve the prices for each as line items and store them in our shoppingCartPrices array for tax calculations
                int j = 0;
                foreach (string CartItemID in shoppingCartContents)
                {
                    // State the items ID
                    if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true") { LogMessage($"foreach item ID:" + CartItemID); }
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"foreach item ID:" + CartItemID);

                    // Get the Price based on the ID
                    string CartItemPrice = Storefront.GetValue("DocumentProperty", "Price", CartItemID);
                    shoppingCartPrices[j++] = Convert.ToDecimal(CartItemPrice);

                    // State the Line Item Price
                    if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true") { LogMessage($"foreach item Price:" + CartItemPrice); }
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"foreach item Price:" + CartItemPrice);
                }

                // Unused
                #region |--Used for testing to ensure the array had the right values--|
                /*
                foreach (decimal itemPrice in shoppingCartPrices)
                {
                    LogMessage($"foreach item Price in shoppingCartPrices:" + itemPrice);
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"foreach item Price in shoppingCartPrices:" + itemPrice);
                }
                */

                #endregion

                #endregion


                // Avalara API call section
                #region |--This is the section that connects and pulls info from avalara--|

                // Check to see if we need to get taxes from Avalara first (We only collect taxes for orders from Georgia currently) 
                // Added 1/15/2019 per Other and Other

                // Check to ensure we only collect taxes from Georgia currently
                if (SState == "GA" || SState == "Georgia" || SState == "georgia")
                {

                    // Shows the section where we change the tax rate in the .txt file
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*                        *");
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*Tax Rates Section Part 2*");
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*                        *");

                    // Set the tax amount based on a few zipcodes and send it back to pageflex in the .txt file
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Tax amount is:           " + taxAmount[0].ToString() + " before we make our Avalara call");

                    // Check if debug mode is turned on; If it is then we log these messages
                    if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                    {
                        // Shows the section where we change the tax rate
                        LogMessage($"*                        *");
                        LogMessage($"*Tax Rates Section Part 2*");
                        LogMessage($"*                        *");

                        // Set the tax amount based on a few zipcodes and send it back to pageflex
                        LogMessage($"Tax amount is:           " + taxAmount[0].ToString() + " before we make our Avalara call");
                    }

                    // Get our Avalara logon info from the storefront
                    string AVEnvironment = Storefront.GetValue("ModuleField", _AV_ENVIRONMENT, _UNIQUE_NAME);
                    string AVCompanyCode = Storefront.GetValue("ModuleField", _AV_COMPANY_CODE, _UNIQUE_NAME);
                    string AVCustomerCode = Storefront.GetValue("ModuleField", _AV_CUSTOMER_CODE, _UNIQUE_NAME);
                    string AVuserID = Storefront.GetValue("ModuleField", _AV_USER_ID, _UNIQUE_NAME);
                    string AVuserPassword = Storefront.GetValue("ModuleField", _AV_USER_PASSWORD, _UNIQUE_NAME);

                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Avalara logon information:");
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Environment:             {AVEnvironment}");
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Company Code:            {AVCompanyCode}");
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Customer Code:           {AVCustomerCode}");
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Avalara User ID:         {AVuserID}");
                    //LMTF.LogMessagesToFile(storeFrontName, LOG_FILENAME1, LOG_FILENAME2, $"Avalara User Password:   {AVuserPassword}");     

                    // Check if debug mode is turned on; If it is then we log these messages
                    if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                    {
                        // Log messages to show what was retrieved from the storefront 
                        LogMessage($"Avalara logon information:");
                        LogMessage($"Environment:             {AVEnvironment}");
                        LogMessage($"Company Code:            {AVCompanyCode}");
                        LogMessage($"Customer Code:           {AVCustomerCode}");
                        LogMessage($"Avalara User ID:         {AVuserID}");
                        //LogMessage($"Avalara User Password:   {AVuserPassword}");      
                    }


                    // Get the current date to pass with transaction call
                    DateTime thisDay = DateTime.Today;

                    // Setup the begining of transaction before passing cart line items
                    string rawJsonAvaStart = @"{""companyCode"": """;
                    rawJsonAvaStart += AVCompanyCode;
                    rawJsonAvaStart += @""", ""type"": ""SalesOrder"", ""date"": """;
                    rawJsonAvaStart += thisDay.ToString("yyyy/MM/dd");
                    rawJsonAvaStart += @""", ""customerCode"": """;
                    rawJsonAvaStart += AVCustomerCode;
                    rawJsonAvaStart += @""", ""addresses"": { ""singleLocation"": { ""line1"": """;
                    rawJsonAvaStart += SAddress1;
                    rawJsonAvaStart += @""", ""line2"": """;
                    rawJsonAvaStart += SAddress2;
                    rawJsonAvaStart += @""", ""city"": """;
                    rawJsonAvaStart += SCity;
                    rawJsonAvaStart += @""", ""region"": """;
                    rawJsonAvaStart += SState;
                    rawJsonAvaStart += @""", ""country"": """;
                    rawJsonAvaStart += SCountry;
                    rawJsonAvaStart += @""", ""postalCode"": """;
                    rawJsonAvaStart += SPostalCode;
                    rawJsonAvaStart += @""" } }, ""lines"": [ ";

                    // Setup the ending of call to add after cart line items
                    string rawJsonAvaEnd = @" } ] }";

                    // Setup variable to hold completed JSON data outside of if statements
                    string rawJsonAvaComplete = "";

                    // Setup strings to build JSON data
                    string Lines = "";
                    string Lines2 = "";


                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Start building JSON line data to pass to Avalara call");
                    if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true") { LogMessage($"Start building JSON line data to pass to Avalara call"); }



                    // Loop through for each line item price value in the cart
                    int h = 0;
                    foreach (decimal CartItemPrice in shoppingCartPrices)
                    {

                        // They are all added  in the same manner, except for the last one which has no comma after it (The last is taken care of in the else with Lines2 variable)
                        if (h < (shoppingCartPrices.Length))
                        {
                            Lines += @"{ ""amount"": " + CartItemPrice + " }, ";

                            // Log
                            LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Line {h} amount: {CartItemPrice}");
                            if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true") { LogMessage($"Line {h} amount: {CartItemPrice}"); }
                        }
                        h++;
                    }

                    // Add the shipping and handling charges from the store to be sent for taxes as well.
                    Lines2 = @"{ ""amount"": " + totalTaxableAmount;

                    // Log
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"Last line (shipping and handling) amount: {totalTaxableAmount}");
                    if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true") { LogMessage($"Last line (shipping and handling) amount: {totalTaxableAmount}"); }


                    // Put all the JSON strings together so they can be sent as one to Avalara 
                    rawJsonAvaComplete = rawJsonAvaStart + Lines + Lines2 + rawJsonAvaEnd;

                    //
                    // Connect to Avalara:
                    //

                    // Specify connection point
                    var client = new RestClient("https://sandbox-rest.avatax.com/api/v2/transactions/create");

                    // Check to see if extension is set to use production or sandbox
                    if (AVEnvironment == "Production" || AVEnvironment == "production")
                    {
                        client = new RestClient("https://rest.avatax.com/api/v2/transactions/create");
                    }
                    else if (AVEnvironment == "Sandbox" || AVEnvironment == "sandbox")
                    {
                        client = new RestClient("https://sandbox-rest.avatax.com/api/v2/transactions/create");
                    }
                    else
                    {
                        // Log
                        LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "The Enviroment field on extension admin page is not setup correctly");
                        LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "The Enviroment field is looking for 'Production' or 'Sandbox'");
                        if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                        {
                            LogMessage("The Enviroment field on extension admin page is not setup correctly");
                            LogMessage("The Enviroment field is looking for 'Production' or 'Sandbox'");
                        }
                    }

                    // Specify we are sending JSON data
                    client.AddDefaultHeader("Content-Type", "application/json");

                    // Specify it is a POST request
                    var request = new RestRequest(Method.POST);

                    // Specify credentials to use for connection (These have to be converted using Base64 encoding first)
                    //request.AddHeader("Authorization", "Basic c3ZjLXBhZ2VmbGV4OkFWRXh0ZW5zaW9uMjAxOQ==");
                    string userCredentials = AVuserID + ":" + AVuserPassword;
                    string EncodedUserCredentials = Base64Encode(userCredentials);
                    //LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, EncodedUserCredentials);
                    request.AddHeader("Authorization", "Basic " + EncodedUserCredentials);

                    // Specify the JSON data string we built to be passed here
                    request.AddParameter("application/json", rawJsonAvaComplete, ParameterType.RequestBody);

                    // Specify a variable to hold the response
                    IRestResponse response = client.Execute(request);

                    //Save the response into a variable
                    string returnResponse = response.Content;

                    // This is used to log the results from our connection to Avalara
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, "JSON reponse returned from our Avalara call: ");
                    LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, returnResponse);
                    if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                    {
                        LogMessage("JSON reponse returned from our Avalara call: ");
                        LogMessage(returnResponse);
                    }

                    // Convert response into JSON object
                    var x = JObject.Parse(returnResponse);

                    // Get the totalTax value pair from the object
                    var taxes = x["totalTax"];

                    // Convert this value into a double
                    tax = Convert.ToDouble(taxes);


                }   // End check if Georgia


                //Set the tax amount on pageflex to the returned value from Avalara
                taxAmount[0] = tax;

                // Log for .txt file
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"The zipcode was:         " + SPostalCode);
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"The new TaxAmount is:    " + taxAmount[0].ToString());

                // Send message saying we have completed this process in the .txt file
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*                        *");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*           End          *");
                LMTF.LogMessagesToFile(storeFrontName, storefrontType, LOG_PART_1, LOG_PART_2, LOG_PART_3, LOG_PART_4, $"*                        *");

                // Check if debug mode is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE] == "true")
                {
                    LogMessage($"The zipcode was:         " + SPostalCode);
                    LogMessage($"The new TaxAmount is:    " + taxAmount[0].ToString());

                    // Send message saying we have completed this process
                    LogMessage($"*                        *");
                    LogMessage($"*           End          *");
                    LogMessage($"*                        *");
                }

                // Check if debug mode 2 is turned on; If it is then we log these messages
                if ((string)ModuleData[_AT_DEBUGGING_MODE2] == "true")
                {
                    // Log that the extension has calculated and retrieved the new tax rate from Avalara
                    LogMessage($"Avalara Tax Extension has successfully connected to Avalara and retrieved the new tax amount");

                    // Log that the extension is done being called  
                    LogMessage($"Completed Avalara Tax Extension Process for order: {OrderID}");
                }

                #endregion


                return eSuccess;



            }   // End else for if not on approval page

        }   // End Method CalculateTax2

        #endregion


        // Unused
        #region |--Used when testing to see if the depreciated version would work (It does not work)--|

        /*
        public override int CalculateTax(string orderID, double taxableAmount, double prevTaxableAmount, ref TaxValue[] tax)
        {
            // Used to help to see where these mesages are in the Storefront logs page
            LogMessage($"*      space       *");            
            LogMessage($"*      START       *");    
            LogMessage($"*      space       *");

            // Shows what values are passed at beginning
            LogMessage($"OrderID is: {orderID}");                    
            LogMessage($"TaxableAmount is: {taxableAmount.ToString()}");            
            LogMessage($"prevTaxableAmount is: {prevTaxableAmount.ToString()}");   

            return eSuccess;
        }
        */

        #endregion


        // Function used to encode data to Base64
        #region |--This is a function used to encode the username and password into Base64--|
        // There were several ways to send for authorization, but this seemed to be the best solution at the time
        public static string Base64Encode(string plainText)
        {

            // Check for null values passed (would happen if no one fills in the fields on the admin page of extension)
            if (plainText == null)
            {
                return null;
            }

            // Convert the string passed into bytes (encoding only works on bytes)
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);

            // return the Base64 converted string
            return Convert.ToBase64String(plainTextBytes);
        }
        #endregion


    }   // End of the class: ExtensionSeven

}   // End of the file
