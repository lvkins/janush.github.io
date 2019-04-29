﻿using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.XPath;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace PromoSeeker.Core
{
    public enum ProductLoadResultType
    {
        /// <summary>
        /// Product is found valid.
        /// </summary>
        Ok,

        /// <summary>
        /// Lack of the response occurred.
        /// </summary>
        NoResponse,

        /// <summary>
        /// Invalid response occurred.
        /// </summary>
        InvalidResponse,

        /// <summary>
        /// Detected no product at all.
        /// </summary>
        ProductNotDetected,

        ProductUnknownName,
        ProductUnknownPrice,
        ProductUnknownCulture,

        /// <summary>
        /// Product was detected, but we wasn't able to acquire some of it's parameters.
        /// </summary>
        ProductParamNotFound,
        ProductNeedValidPrice,
    }

    /// <summary>
    /// The product load result object.
    /// </summary>
    public class ProductLoadResult
    {
        public bool Success { get; set; }

        public string Error { get; set; }

        public ProductLoadResultType ErrorType { get; set; }

        public IList<IPriceInfo> PricesDetected { get; set; }

        public List<string> NamesDetected { get; set; }
    }

    /// <summary>
    /// A main product class.
    /// </summary>
    public class Product : IDisposable
    {
        #region Private Members

        /// <summary>
        /// The document of the product web page.
        /// </summary>
        private IDocument mHtmlDocument;

        /// <summary>
        /// The tracking task cancellation token.
        /// </summary>
        private CancellationTokenSource _trackingCancellation;

        #endregion

        #region Public Properties

        /// <summary>
        /// The product URL.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// The product name.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// The price currency amount representation formatted by the provider in the <see cref="Culture"/>.
        /// </summary>
        public string Price => PriceInfo.Price.Decimal.ToString("C", Culture);

        /// <summary>
        /// The current product price informations.
        /// </summary>
        public IPriceInfo PriceInfo { get; private set; }

        /// <summary>
        /// The selector used to grab the price value from the document.
        /// </summary>
        public string PriceXPathOrSelector { get; private set; }

        /// <summary>
        /// The product price history.
        /// </summary>
        public List<double> PriceHistory { get; set; }

        /// <summary>
        /// The culture of the website where the product is.
        /// </summary>
        public CultureInfo Culture { get; private set; }

        /// <summary>
        /// The product site full title.
        /// </summary>
        public string Title { get; private set; }

        /// <summary>
        /// Whether the product data is stored in the database.
        /// </summary>
        public bool IsSaved { get; private set; }

        /// <summary>
        /// Whether the product properties are loaded and set.
        /// </summary>
        public bool IsLoaded => mHtmlDocument != null && PriceInfo != null && !string.IsNullOrEmpty(PriceXPathOrSelector);

        /// <summary>
        /// Whether the product properties like the culture of the website, price selector and name should be loaded automatically.
        /// </summary>
        public bool IsAutoLoadProperties { get; }

        /// <summary>
        /// The task that is handling tracking of this product.
        /// </summary>
        public Task TrackingTask { get; private set; }

        #endregion

        #region Public Events

        /// <summary>
        /// The event that raises when application failed to load product properties.
        /// </summary>
        public event Action LoadFailed = () => { };

        /// <summary>
        /// The event that raises when tracking was unexpectedly aborted.
        /// </summary>
        public event Action<Exception> TrackingFailed = (exception) => { };

        /// <summary>
        /// The event that raises when the product is updating.
        /// </summary>
        public event Action Updating = () => { };

        /// <summary>
        /// The event that raises when the product was updated.
        /// </summary>
        public event Action<ProductUpdateResult> Updated = (result) => { };

        #endregion

        #region Constructor

        /// <summary>
        /// Automatically attempts loads the product by given <paramref name="url"/>
        /// </summary>
        /// <param name="url">The product URL.</param>
        public Product(string url)
        {
            // Set product URL
            Url = url;

            // Auto load other necessary properties
            IsAutoLoadProperties = true;

            /*
            // Create HTML request
            var request = new HtmlRequest();

            // Load website URL
            _ = request.LoadAsync(url);

            #region Ensure Valid Response

            // If status code is any other than 200...
            if (request.Response?.StatusCode != System.Net.HttpStatusCode.OK)
            {
                // Cannot continue
                throw new Exception("Status code not OK");
            }

            #endregion

            // Store the HTML document
            mHtmlDocument = request.Document;

            // Acquire Promotion Data
            if (AutoSetup)
            {
                Setup();
            }
            */
        }

        /// <summary>
        /// Loads the product by manually specified data.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="url"></param>
        /// <param name="priceXPath"></param>
        /// <param name="culture"></param>
        public Product(string name, string url, string priceXPath, CultureInfo culture)
        {
            // We are setting the properties manually
            IsAutoLoadProperties = false;

            // Set the properties
            Name = name;
            Url = url;
            PriceXPathOrSelector = priceXPath;
            Culture = culture;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Loads the product.
        /// </summary>
        /// <returns>The result of the product load.</returns>
        public async Task<ProductLoadResult> LoadAsync()
        {
            if (string.IsNullOrEmpty(Url))
            {
                throw new InvalidOperationException("Url not specified");
            }

            // Attempt to load HTML
            try
            {
                // Wait for the HTML document
                mHtmlDocument = await WebLoader.LoadReadyAsync(Url);
            }
            catch (Exception e)
            {
                // Let developer know
                Debugger.Break();

                // Log details
                CoreDI.Logger.Exception(e);
            }

            // If status code is not successful..
            if (mHtmlDocument == null || !((int)mHtmlDocument.StatusCode >= 200 && (int)mHtmlDocument.StatusCode <= 299))
            {
                // Return with the result
                return new ProductLoadResult
                {
                    // Load wasn't successful
                    Success = false,
                    // Set no response or invalid response,
                    ErrorType = mHtmlDocument == null
                        ? ProductLoadResultType.NoResponse
                        : ProductLoadResultType.InvalidResponse,
                };
            }

            // Once we have the HTML document, we can parse product properties

            // If properties should be automatically detected...
            return IsAutoLoadProperties
                // Load automatically
                ? await LoadAutoAsync()
                // Load manually
                : await LoadManuallyAsync();
        }

        /// <summary>
        /// Automatically loads the latest product properties like name, price, culture info.
        /// </summary>
        /// <returns>A task that will finish once auto loading is finished. Task result contains the result of the load.</returns>
        private Task<ProductLoadResult> LoadAutoAsync()
        {
            // Create the result
            var result = new ProductLoadResult { Success = true };

            // NOTE: Order of the loading properties is important

            // If the culture is not specified and we are not able to detect it...
            if (!DetectCulture())
            {
                result.Success = false;
                result.ErrorType = ProductLoadResultType.ProductUnknownCulture;
            }
            // If the name is not specified and we are not able to detect it...
            else if (!DetectName())
            {
                result.Success = false;
                result.ErrorType = ProductLoadResultType.ProductUnknownName;
            }
            // Once we have culture and name, we can detect price
            else
            {
                // Detect prices sources
                var prices = DetectPriceSources();

                ;

                // If no price sources were found...
                if (!prices.Any())
                {
                    result.Success = false;
                    result.ErrorType = ProductLoadResultType.ProductUnknownPrice;
                }
                else if (prices.Count == 1)
                {
                    // We have the price source
                    PriceInfo = prices.First();
                }
                // Else if we have more than one price detected...
                // TODO: Check score
                else
                {
                    result.Success = false;

                    // Threat is as an error - user should be prompted to pick a valid one.
                    result.ErrorType = ProductLoadResultType.ProductNeedValidPrice;

                    // Add the prices detected to the result
                    result.PricesDetected = prices;
                }
            }

            // Return the result
            return Task.FromResult(result);
        }

        /// <summary>
        /// Manually loads the latest product properties like name, price.
        /// </summary>
        /// <returns>A task that will finish once loading is finished. Task result contains the result of the load.</returns>
        private Task<ProductLoadResult> LoadManuallyAsync()
        {
            // Create the result
            var result = new ProductLoadResult { Success = true };

            // Ensure we got all necessary properties set
            if (Culture == null || string.IsNullOrEmpty(Name) || string.IsNullOrEmpty(PriceXPathOrSelector))
            {
                result.Success = false;
                result.ErrorType = ProductLoadResultType.ProductParamNotFound;
                return Task.FromResult(result);
            }

            #region Set Price

            // Select node by given xpath query
            var node = (HtmlElement)mHtmlDocument.Body.SelectSingleNode(PriceXPathOrSelector);

            // Because the price xpath query can point to the meta tag, where the price lies
            // In case of a meta tag node, read the 'content' attribute, instead of the inner text.
            var isMeta = node?.NodeName.ToUpperInvariant() == "META";

            // Get the value
            var value = isMeta
                ? node.GetAttribute("content")
                : node.TextContent;

            // If unable to read the node value...
            if (!ReadPrice(value, out var priceValue))
            {
                result.Success = false;
                result.ErrorType = ProductLoadResultType.ProductUnknownPrice;

                // Unable to parse price
                return Task.FromResult(result);
            }

            // Set product price
            PriceInfo = new PriceInfo
            {
                AttributeName = isMeta ? "content" : null,
                Price = priceValue,
                Source = isMeta ? PriceSourceType.PriceSourceAttribute : PriceSourceType.PriceSourceText,
                SourceNode = node
            };

            #endregion

            // Return the result
            return Task.FromResult(result);
        }

        /// <summary>
        /// Refreshes the product properties (eg. the current price)
        /// </summary>
        public async Task RefreshAsync()
        {
            // If the product is not loaded...
            if (!IsLoaded)
            {
                throw new InvalidOperationException("Product has to be loaded in order to refresh");
            }


        }

        /// <summary>
        /// Sets up the promotion product data.
        /// </summary>
        public void Setup()
        {
            // Find language and set the culture
            if (DetectCulture())
            {
                // Leave a log message
                Console.WriteLine($">> Culture detected: {Culture.EnglishName} ({Culture.Name})");
            }
            else
            {
                // Fallback to default culture
                //Console.WriteLine("> No culture was detected. Fallback to local culture");
                //mCulture = CultureInfo.CurrentCulture;

                // Cannot continue without valid culture.
                // TODO: Prompt the user to specify the culture/currency the website is using manually.
                throw new NullReferenceException("Culture not set");
            }

            // Find the product name
            DetectName();

            // Find the product price
            DetectPriceSources();
        }

        /// <summary>
        /// Starts the product tracking task with the specified time <paramref name="interval"/>.
        /// </summary>
        /// <param name="interval">The time interval to track the product within.</param>
        public async Task TrackAsync(TimeSpan interval)
        {
            // If a tracking task that is not completed already exists...
            if (TrackingTask != null && !TrackingTask.IsCompleted)
            {
                // Do nothing, shall properly stop tracking first
                return;
            }

            // Dispose of any existing cancellation token
            _trackingCancellation?.Dispose();

            // Create fresh token
            _trackingCancellation = new CancellationTokenSource();

            // Create a task
            TrackingTask = Task.Run(async () =>
            {
                // Do this until not interrupted by cancellation token
                while (true)
                {
                    // Wait for the specified amount of time
                    await Task.Delay(interval, _trackingCancellation.Token);

                    // Abort if cancellation is requested
                    _trackingCancellation.Token.ThrowIfCancellationRequested();

                    // Raise updating event
                    Updating();

                    // Refresh product

                    // Load result
                    var result = await LoadAsync();

                    // Raise updated event
                    Updated(new ProductUpdateResult
                    {
                        Success = result.Success
                    });
                }
            }, _trackingCancellation.Token)
            // Handle faulted task
            .ContinueWith(t =>
            {
                // Inform developer
                Debugger.Break();

                // Notify that the tracking task has failed and pass the exception details
                TrackingFailed(t.Exception?.GetBaseException());
            }, TaskContinuationOptions.OnlyOnFaulted)
            .ContinueWith(t =>
            {
                // Handle task cancellation politely and stop bubbling it up
            }, TaskContinuationOptions.OnlyOnCanceled);

            // Wait for the task to finish
            await TrackingTask;
        }

        /// <summary>
        /// Stops the product tracking task.
        /// </summary>
        public async Task StopTrackingAsync()
        {
            // Request task cancellation
            _trackingCancellation?.Cancel();

            // Wait while task is completing...
            while (TrackingTask != null && !TrackingTask.IsCompleted)
            {
                await Task.Delay(100);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Attempts to locate website culture in order to find which culture to use for price conversions etc. If the culture couldn't be located
        /// the default culture of <see cref="CultureInfo.CurrentCulture"/> will be set.
        /// </summary>
        /// <returns><see langword="true"/> if the culture was located in the <see cref="mHtmlDocument"/> and set, otherwise <see langword="false"/>.</returns>
        private bool DetectCulture()
        {
            #region Detect Culture By Currency Meta Tag

            // Leave a log message
            Debug.WriteLine("> Attempt to detect culture by currency meta tag...");

            // Find the currency value in the pre-defined node sources
            var currencyValue = mHtmlDocument.Body.FindContentFromSource(Consts.CURRENCY_SOURCES);

            // If not empty and culture exists...
            if (!string.IsNullOrWhiteSpace(currencyValue) && CurrencyHelpers.FindCultureByCurrencySymbol(currencyValue, out var culture))
            {
                // Set the culture
                Culture = culture;

                // Success
                return true;
            }

            #endregion

            #region Detect Culture By Prices Currency

            // Leave a log message
            Debug.WriteLine("> Attempt to detect culture by prices currency...");

            // Take all prices in the document, lookup the currency symbol they use and try to find appropriate culture by the symbol.
            var topPricesCulture = mHtmlDocument.Descendents()
                // Get most nested text nodes
                .Where(_ => !_.HasChildNodes && !string.IsNullOrWhiteSpace(_.TextContent))
                // Get fixed text
                .Select(_ =>
                {
                    // Prepare text
                    ParseNodeText(_.TextContent, out var result);
                    return new { Text = result, Node = _ };
                })
                // Where the value is price-like.
                // Content is not empty and has at least one digit and character, but not more than 5 non-digit characters (longest currency symbol length) + 5 in reserve for separators.
                // NOTE: We cannot use char.IsLetter check here because we are searching for either a symbol ($) or ISO symbol (USD), also some symbol characters are not recognized by .IsLetter.
                .Where(_ => !string.IsNullOrEmpty(_.Text) && _.Text.Any(char.IsDigit) && _.Text.Count(c => !char.IsWhiteSpace(c) && !char.IsDigit(c)).InRange(1, 10))
                // Get the culture
                .Select(_ =>
                {
                    // Get culture from the price currency symbol
                    CurrencyHelpers.FindCurrencySymbol(_.Text, out var _, out var result);

                    return new
                    {
                        SourceText = _.Text,
                        Culture = result,
                    };
                })
                // Where culture was found
                .Where(_ => _.Culture != null)
                // Group by culture identifier
                .GroupBy(_ => _.Culture.LCID)
                // Get counts
                .Select(_ => new { _.First().Culture, Count = _.Count() })
                // Order by descending count value
                .OrderByDescending(_ => _.Count)
                // Get top result
                .FirstOrDefault();

            // If culture was determined...
            if (topPricesCulture != null)
            {
                // Set the culture
                Culture = topPricesCulture.Culture;

                // Success
                return true;
            }

            #endregion

            #region Detect Culture By Language Definition

            // Leave a log message
            Debug.WriteLine("> Attempt to detect culture by language definition...");

            // Last chance - not accurate - because website language doesn't necessarily mean currency.
            // Attempt to locate website culture in order to find which culture to use for price conversions etc.
            foreach (var source in Consts.LANG_SOURCES)
            {
                // Select node by XPath expression
                var node = (HtmlElement)mHtmlDocument.DocumentElement.SelectSingleNode(source.Key);

                // If node exists...
                if (node != null)
                {
                    // Find any attribute
                    var result = source.Value
                        .Select(_ => new { Name = _, Value = node.GetAttribute(_) })
                        .FirstOrDefault(_ => !string.IsNullOrWhiteSpace(_.Value));

                    // If an attribute was found...
                    if (result != null)
                    {
                        //Debug.WriteLine($"> Found language attribute ({result.Name}) -> [{result.Value}]");

                        // Check if such culture name can be found
                        var exists = CultureInfo.GetCultures(CultureTypes.AllCultures)
                            .Any(_ => string.Equals(_.Name, result.Value, StringComparison.OrdinalIgnoreCase));

                        // If exists...
                        if (exists)
                        {
                            // Set website culture
                            Culture = new CultureInfo(result.Value);

                            // Success
                            return true;
                        }
                    }
                }
            }

            #endregion

            // Failure
            return false;
        }

        /// <summary>
        /// Attempts to extract and set the displaying product name from the <see cref="mHtmlDocument"/>.
        /// </summary>
        /// <returns><see langword="true"/> if the product name was extracted and set successfully, otherwise <see langword="false"/>.</returns>
        private bool DetectName()
        {
            // We assume that the product name is always defined in the page title.
            // Otherwise we are dealing with some badly set e-commerce site, which we don't care about right now.

            // Find the title
            var titleValue = mHtmlDocument.DocumentElement.FindContentFromSource(Consts.TITLE_SOURCES);

            // If we have no page title...
            if (string.IsNullOrWhiteSpace(titleValue))
            {
                return false;
            }

            // Format the page title properly
            ParseNodeText(titleValue, out var pageTitle);

            // Get nodes that can potentially contain the product title
            var contents = mHtmlDocument.DocumentElement.Descendents()
                // Only nodes without children, not empty and not longer than product max. length
                .Where(_ => !_.HasChildNodes && !string.IsNullOrWhiteSpace(_.TextContent) && _.TextContent.Length <= Consts.PRODUCT_TITLE_MAX_LENGTH)
                // Select node along with parsed text
                .Select(_ =>
                {
                    // Parse
                    ParseNodeText(_.TextContent, out var text);

                    // Some shops can format the page title like so:
                    // The Product Name, SiteName.com
                    // Since the product name is 'The Product Name' (without trailing comma)
                    // We need to get rid of it, before comparing.
                    text = text.TrimEnd(',', '.');

                    // Create anonymous type
                    return new { Node = _, Text = text };
                })
                // Ignore empty records after parsing
                .Where(_ => !string.IsNullOrEmpty(_.Text))
                .ToList();

            // Title should occur in the page content at least once.
            // Find title with most occurrences in the document.

            // Split the page title
            var pieces = pageTitle.Split(' ');

            // If we have title pieces...
            if (pieces.Any())
            {
                // Title occurrences counter
                var prevCount = 0;

                // Reduce page title by one word until we find the valid product title
                for (var i = pieces.Length; i != 0; i--)
                {
                    // Get possible title
                    var result = string.Join(" ", pieces, 0, i).TrimEnd(',', '.');

                    // Get occurrences count in the content
                    var count = contents.Count(_ => _.Text.Equals(result, StringComparison.OrdinalIgnoreCase));

                    // If title occurs higher amount of times...
                    if (count > prevCount)
                    {
                        // Set most accurate title
                        pageTitle = result;
                        prevCount = count;
                    }
                }
            }

            // Set product name
            Name = pageTitle;

            // Set product site title
            Title = titleValue;

            // Leave a message
            Console.WriteLine($"> Setting product name: {Name}");

            // Setting the title was successful if it's not empty
            return !string.IsNullOrWhiteSpace(Name);
        }

        /// <summary>
        /// Attempts to extract all available price source from the <see cref="mHtmlDocument"/>.
        /// </summary>
        /// <returns>A list of <see cref="IPriceInfo"/> objects.</returns>
        private IList<IPriceInfo> DetectPriceSources()
        {
            // Found price sources
            var ret = new List<IPriceInfo>();

            // Iterate through pre-defined price sources
            // Prices from this source are the most eligible
            foreach (var source in Consts.PRICE_SOURCES)
            {
                // Find a node in the document
                // If node exists...
                if (mHtmlDocument.DocumentElement.SelectSingleNode(source.Key) is HtmlElement node)
                {
                    // Whether the price is defined within the attribute
                    var isAttribute = !string.IsNullOrEmpty(source.Value);

                    // If we are dealing with the attribute source...
                    var isPrice = isAttribute
                        // Read from the attribute
                        ? ReadPrice(node.GetAttribute(source.Value), out var price)
                        // Otherwise assume node text
                        : ReadPrice(node.TextContent, out price);

                    // If price read was successful...
                    if (isPrice)
                    {
                        // Read from value attribute and store the price source
                        ret.Add(new PriceInfo
                        {
                            Price = price,
                            SourceNode = node,
                            AttributeName = isAttribute ? source.Value : null,
                            Source = isAttribute
                                ? PriceSourceType.PriceSourceAttribute
                                : PriceSourceType.PriceSourceText,
                        });
                    }
                }
            }

            // If we have any prices from the pre-defined sources...
            if (ret.Any())
            {
                // We can return with a good price sources at this point
                return ret;
            }

            // If the above failed, attempt to locate price by parsing the document.
            // Some e-commerce sites just make it hard sometimes and decide not to use the good practices.

            // Compiled regex to find a product name in the document.
            var ProductNameRegex = new Regex($@"\b{Regex.Escape(Name)}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // Compiled regex to find the Javascript objects values in the document, where key contains either a 'price' or 'cost' word.
            // Sample matches:
            //  ['myPrice' : 123.00,] -> 123.00
            //  ['productCost' : 1234,] -> 1234
            //  ["cost" : 100,] -> 100
            var PricesInJavaScriptRegex = new Regex(@"\b[\""\']?(?:[\w\-]+)?(?:price|cost)(?:[\w\-]+)?[\""\'\s]?\:[\""\'\s]?([\d\.\,\ ]+)[\""\']?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // The maximum allowed number of depth between price and the product name price node
            var MaxNameNodeDistance = 7;

            // We extract the prices in three ways:
            //
            //  1. The Javascript objects values, if the key contains 'price' keyword.
            //     Some e-commerce sites declare the prices in the Javascript objects.
            //     
            //     Will match those:
            //
            //      *word boundary*
            //      'mightBeAPriceIHope' : 1234,
            //      "price" : 4321,
            //      *word boundary*
            //
            //  2. The values from attributes named after price, eg. data-my-price="1234".
            //     Some e-commerce sites declare the prices in the tag attributes. Accurate or not,
            //     this is a worthy method to get a valid price from.
            //
            //
            //  3. Parse most nested nodes inner text that display the price on the screen.
            //     Every e-commerce site has to print the price to the user.
            //     Despite some cases where the price is shown through images/drawing. This is a really good source of information.
            //

            // Extract price values in the Javascript objects declared in the document
            var pricesInJavaScript = PricesInJavaScriptRegex
                .Matches(mHtmlDocument.DocumentElement.TextContent)
                .Cast<Match>()
                .Select(m => new { IsPrice = ReadPrice(m.Groups[1].Value, out var price), Price = price })
                .Where(_ => _.IsPrice && _.Price.Decimal > 0)
                .Select(_ => new PriceInfo
                {
                    Price = _.Price,
                    Source = PriceSourceType.PriceSourceJavascript,
                });

            // Get all document descendants
            var docDescendants = mHtmlDocument.Descendents();

            // Extract price values from attributes named after prices.
            var pricesInAttributeValues = docDescendants
                // HTML elements descendants only
                .OfType<IHtmlElement>()
                // Having at least one attribute
                .Where(_ => _.Attributes.Any())
                .SelectMany(_ => _.Attributes
                    .Where(a => a.Name.ContainsAny(Consts.PRICE_ATTRIBUTE_NAMES))
                    .Select(a => new { IsPrice = ReadPrice(a.Value, out var price), Price = price, Attribute = a })
                    .Where(a => a.IsPrice && a.Price.Decimal > 0)
                    .Select(a =>
                    {
                        return new PriceInfo
                        {
                            Price = a.Price,
                            Source = PriceSourceType.PriceSourceAttribute,
                            AttributeName = a.Attribute.Name,
                            SourceNode = _,
                        };
                    })
                );

            // Extract price values from most nested nodes
            var pricesInNodes = docDescendants
                // Get text nodes only
                .OfType<IText>()
                // Where text length of least two characters and parent is not a link
                .Where(_ => _.Length > 1 && _.ParentElement.IsLink() == false)
                // Select price
                .Select(_ =>
                {
                    return new
                    {
                        IsPrice = ReadPrice(_.TextContent, out var price),
                        PriceSource = new PriceInfo
                        {
                            Price = price,
                            Source = PriceSourceType.PriceSourceText,
                            SourceNode = _.ParentElement as IHtmlElement,
                        }
                    };
                })
                // Get only successfully parsed prices
                .Where(_ => _.IsPrice && _.PriceSource.Price.Decimal > 0)
                .Select(_ => _.PriceSource);

            // Get best prices by concatenating three source lists and scoring each price group.
            var bestPrices = pricesInJavaScript
                .Concat(pricesInAttributeValues)
                .Concat(pricesInNodes)
                // Attempt to find a currency symbol in each price
                .Select(_ =>
                {
                    // Distance from the price node stream position to the closest product name node stream position.
                    int? nameDistance = null;

                    // If price has a source node and source price is a price found in the node text (node that *could be* displayed to user)...
                    if (_.SourceNode != null && _.Source == PriceSourceType.PriceSourceText)
                    {
                        // NOTE: Since we've switched from HtmlAgilityPack to AngleSharp
                        // We have lost the ability to check the stream position of the element.
                        // Thats why we use the node 'depth' approach to get the distance - it's not that accurate however.
                        // Stream position should be added soon: https://github.com/AngleSharp/AngleSharp/issues/754

                        // Find closest product name position
                        var result = _.SourceNode.FindClosest(n =>
                        {
                            return ProductNameRegex.IsMatch(n.TextContent);
                            //return n.InnerText.ContainsEx(ProductName, StringComparison.Ordinal);
                        }, out var node, out var distance);

                        // If closest node was found...
                        if (result)
                        {
                            // Set a distance to the node
                            nameDistance = distance; //_.SourceNode.StreamPosition() - distanceToProductName;
                        }
                    }

                    // Pass the anonymous type
                    return new
                    {
                        PriceSource = _,
                        NameDistance = nameDistance,
                    };
                })
                // Group entries by the price
                .GroupBy(_ => _.PriceSource.Price.Decimal)
                // Select price & entries count
                .Select(_ =>
                {
                    // Particular source counts
                    int inAttrCount = 0, inJSCount = 0, inNodeCount = 0, totalCount = 0;

                    // Get particular prices by source count
                    foreach (var item in _)
                    {
                        if (item.PriceSource.Source == PriceSourceType.PriceSourceAttribute)
                        {
                            ++inAttrCount;
                        }
                        else if (item.PriceSource.Source == PriceSourceType.PriceSourceJavascript)
                        {
                            ++inJSCount;
                        }
                        else if (item.PriceSource.Source == PriceSourceType.PriceSourceText)
                        {
                            ++inNodeCount;
                        }

                        ++totalCount;
                    }

                    // Check if any price in this group occurs with the currency symbol in the document...
                    // Depending on the website and how the price is presented to the user, at least once price should have a currency symbol.
                    var hasSymbol = _.Any(p => !string.IsNullOrEmpty(p.PriceSource.Price.CurrencySymbol));

                    #region Compute Price Group Score

                    // Base score
                    var groupScore = totalCount;

                    // We assume, the price appears in either the attributes or the JS code at least once...
                    if (inAttrCount != 0 || inJSCount != 0)
                    {
                        // If all prices don't come from attributes...
                        if (inAttrCount != totalCount)
                        {
                            // Bonus score for prices found in the attributes...
                            groupScore += inAttrCount * 2;
                        }
                        // Otherwise...
                        else
                        {
                            // Weak source penalty
                            groupScore -= (inAttrCount + 20);
                        }

                        // If all prices don't come from JS code...
                        if (inJSCount != totalCount)
                        {
                            // Bonus score for prices found in the JS code...
                            groupScore += inJSCount * 3;
                        }
                        // Otherwise...
                        else
                        {
                            // Weak source penalty
                            groupScore -= (inJSCount + 20);
                        }
                    }
                    // Otherwise if all prices come from the attributes/JS code...
                    else
                    {
                        // Weak source penalty
                        groupScore -= 5;
                    }

                    // Score closest price-to-name element distance

                    // If at least one price comes from a HTML node...
                    if (inNodeCount > 0)
                    {
                        // Get the closest distance of a price to the product name in this group
                        var closestNameDistance = _.Where(p => p.NameDistance > -1).Min(p => p.NameDistance);

                        // If no name element was located near the price...
                        if (closestNameDistance == null)
                        {
                            // Set for penalty
                            closestNameDistance = MaxNameNodeDistance + 1;
                        }

                        // If the distance is relatively close...
                        if (closestNameDistance > 0 && closestNameDistance <= MaxNameNodeDistance)
                        {
                            // Add bonus score
                            groupScore += 10 - (closestNameDistance.Value - 1);
                        }
                        // Otherwise, if the distance is long...
                        else if (closestNameDistance > MaxNameNodeDistance)
                        {
                            // Long distance penalty
                            // 10 points for each exceeded limit
                            groupScore -= closestNameDistance.Value / MaxNameNodeDistance * 10;
                        }
                    }
                    else
                    {
                        // Weak source penalty
                        groupScore -= 5;
                    }

                    // If any price in this group occurs with a currency symbol...
                    if (hasSymbol)
                    {
                        // Add bonus points
                        groupScore += 15;
                    }

                    #endregion

                    // Return the anonymous type containing useful data
                    return new
                    {
                        Price = _.Key,
                        Count = totalCount,
                        InAttrCount = inAttrCount,
                        InJSCount = inJSCount,
                        Score = groupScore,
                        HasSymbol = hasSymbol,
                        Source = _.Select(s => s.PriceSource),
                    };
                })
                // Order by the group of prices score
                .OrderByDescending(_ => _.Score)
                // Take 10 best prices
                //.Take(10)
                // Make list
                .ToList();

            if (!bestPrices.Any())
            {
                Console.WriteLine("> Couldn't detect product price.");
                return null;
            }

            Console.WriteLine("> Prices detected");

            foreach (var item in bestPrices)
            {
                Console.WriteLine($" >> {item.Price} ({item.Score})");
                ret.AddRange(item.Source);
            }

            return ret;
        }

        /// <summary>
        /// Formats the input string by removing any line breaks, multiple whitespaces, then trims the output.
        /// </summary>
        /// <param name="input">The input string to be formatted.</param>
        /// <param name="result">When this method returns, contains the formatted <see cref="string"/> value of the <paramref name="input"/>.</param>
        private void ParseNodeText(string input, out string result) => result = Regex.Replace(HttpUtility.HtmlDecode(input), @"\s+", " ").Trim();

        /// <summary>
        /// Checks whether the input can be parsed to the <see cref="decimal"/> value using the website detected culture <see cref="Culture"/>.
        /// </summary>
        /// <param name="input">The input to be parsed.</param>
        /// <returns><see langword="true"/> if the <paramref name="input"/> value can be parsed to <see cref="decimal"/> value, otherwise <see langword="false"/>.</returns>
        private bool ReadPrice(string input, out PriceValue output)
        {
            // Ensure the proper input
            ParseNodeText(input, out var result);

            // If result is empty...
            if (string.IsNullOrEmpty(result))
            {
                // Not a price
                output = default(PriceValue);
                return false;
            }

            // Fix price format (separators) accordingly to the current culture
            output = CurrencyHelpers.ReadPriceValue(input, Culture);

            // Return with the price valid info
            return output.Valid;
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Cleans up the resources used.
        /// </summary>
        public void Dispose()
        {
            mHtmlDocument?.Dispose();
        }

        #endregion
    }
}
