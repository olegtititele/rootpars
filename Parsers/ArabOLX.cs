using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;


using Modules;
using PostgreSQL;

namespace Parser
{
    public class ArabOLX
    {
        private static string? userPlatform;
        private static string? userLink;
        private static string? userSellerTotalAds;
        private static string? userSellerRegDate;
        private static decimal userSellerRating;
        private static string? userSellerType;
        private static string? blacklist;
        private static string? parserCategory;
        private static DateTime exactTime;
        private static string allCatLink = "https://www.olx.com.om/en/ads/";
        private static string userAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/104.0.0.0 Safari/537.36";
        private static string errorImageUri = "https://upload.wikimedia.org/wikipedia/commons/9/9a/%D0%9D%D0%B5%D1%82_%D1%84%D0%BE%D1%82%D0%BE.png";
        public static string[] blacklistCategories = {"https://www.olx.com.om/en/services/", "https://www.olx.com.om/en/vehicles/cars/", "https://www.olx.com.om/en/vehicles/motorcycles/", "https://www.olx.com.om/en/vehicles/boats/", "https://www.olx.com.om/en/vehicles/trucks/", "https://www.olx.com.om/en/vehicles/other-vehicles/", "https://www.olx.com.om/en/properties/", "https://www.olx.com.om/en/pets/", "https://www.olx.com.om/en/jobs-services/", "https://www.olx.com.om/en/business-industrial/", "https://www.olx.com.om/en/services/"};
        private static HtmlWeb web = new HtmlWeb();
        

        public static async void StartParsing(ITelegramBotClient botClient, long userId, DateTime userExactTime)
        {
            exactTime = userExactTime;
            userPlatform = DB.GetPlatform(userId);
            userLink = DB.GetLink(userId);
            userSellerTotalAds = DB.GetSellerTotalAds(userId);
            userSellerRegDate = DB.GetSellerRegDate(userId);
            userSellerRating = DB.GetSellerRating(userId);
            userSellerType = DB.GetSellerType(userId);
            blacklist = DB.GetBlackList(userId);
            parserCategory = DB.GetParserCategory(userId);
            int timeout = DB.GetTimeout(userId)*1000;

            web.UserAgent = userAgent;

            try
            { 
                List<string> passedLinks = new List<string>();
  
                while(true)
                {
                    try
                    {
                        int page = 1;

                        await ParseCategory(botClient, userId, page, passedLinks);
                        System.Threading.Thread.Sleep(timeout);
                    }
                    catch
                    {
                        System.Threading.Thread.Sleep(timeout);
                    }
                }
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
                DB.UpdateParser(userId, "Stop");
                return;
            }
        }

        private static async Task ParseCategory(ITelegramBotClient botClient, long userId, int page, List<string> passedLinks)
        {
            string adLink = "";
            string categoryLink = GenerateLink(page);
            HtmlDocument document = web.Load(categoryLink);
                    
            var advertisements = document.DocumentNode.SelectNodes("//li[@aria-label=\"Listing\"]");
            if(advertisements != null)
            {
                // int maxTasks = 20;
                // using SemaphoreSlim semaphore = new SemaphoreSlim(maxTasks);
                // List<Task> tasks = new List<Task>();


                foreach (HtmlNode advertisement in advertisements)
                {
                    
                    if(DB.GetParser(userId) == "Start")
                    {
                        try
                        {
                            var aaa = advertisement.SelectSingleNode(".//div[@aria-label=\"Featured\"]//span[@class=\"_151bf64f\"]").InnerHtml;
                            continue;
                        }
                        catch
                        {
                            adLink = "https://www.olx.com.om" + advertisement.SelectSingleNode(".//div[@class=\"ee2b0479\"]//a").GetAttributeValue("href", "");
                            
                            if(passedLinks.Contains(adLink))
                            {
                                Console.WriteLine($"Уже пройдно: {adLink}");
                                return;
                            }
                            else
                            {
                                passedLinks.Add(adLink);
                                Console.WriteLine($"Новая: {adLink}");
                                if(!DB.CheckAdvestisement(userId, adLink))
                                {
                                    // await semaphore.WaitAsync();

                                    // tasks.Add(Task.Run(async () =>
                                    // {
                                    // Console.WriteLine(adLink);
                                        if(await ParsePageInfo(botClient, userId, adLink) == "RestartCategory")
                                        {
                                            return;
                                        }
                                        // semaphore.Release();
                                    // }));
                                }
                            }
                        }
                    }
                    else
                    {
                        passedLinks = new List<string>();
                        DB.UpdateParser(userId, "Stop");
                        return;
                    }
                }
                // await Task.WhenAll(tasks);
                
                page++;
                await ParseCategory(botClient, userId, page, passedLinks);
                // await Task.WhenAll(tasks);
            }
        }

        static async Task<string> ParsePageInfo(ITelegramBotClient botClient, long userId, string adLink)
        {
            string adCategory = "";
            string adPrice = "";
            string adTitle = "";
            string adImage = "";
            string sellerType = "";
            string sellerLink = "";
            string sellerName = "";
            string adDescription = "";
            string adLocation = "";
            int sellerTotalAds = 1;
            DateTime adRegDate = DateTime.Today;
            DateTime sellerRegDate = DateTime.Today;


            HtmlDocument document = web.Load(adLink);

            var scripts = document.DocumentNode.SelectNodes("//script");

            foreach (HtmlNode script in scripts)
            {
                if(script.InnerHtml.Contains("priceValidUntil"))
                {
                    string json = script.InnerText;
                    JObject jObject = JObject.Parse(json);
                    adRegDate = Convert.ToDateTime(jObject["offers"]![0]!["priceValidUntil"]!.ToString()).AddDays(-30).AddHours(3);
                    break;
                }
            }

            if(Functions.CheckAdRegDate(exactTime, adRegDate)){  }else{ return "RestartCategory"; }

            var categories = document.DocumentNode.SelectNodes("//li[@itemprop=\"itemListElement\"]");
            
            foreach(var category in categories)
            {
                try
                {
                    adCategory = "https://www.olx.com.om" + category.SelectSingleNode(".//a[@itemprop=\"item\"]").GetAttributeValue("href", "");

                    if(blacklistCategories.Contains(adCategory))
                    {
                        return "NextAdvertisement";
                    }
                }
                catch
                {
                    continue;
                }
            }
            
            string adId = adLink.Split("ID")[1].Split('.')[0];

            var sellerPhoneNumber = await GetPhoneNumber(adId);

            if(sellerPhoneNumber == "null")
            {
                return "NextAdvertisement";
            }

            if(Functions.CheckBlacklistAds(userId, sellerPhoneNumber, blacklist!)){ }else{ return "NextAdvertisement"; }


            foreach (HtmlNode script in scripts)
            {
                if(script.InnerHtml.Contains("window['dataLayer'] = window['dataLayer'] || [];window['dataLayer'].push("))
                {
                    string json = script.InnerHtml.Split("window['dataLayer'] = window['dataLayer'] || [];window['dataLayer'].push(")[1];
                    char[] MyChar = {';',')'};
                    json = json.TrimEnd(MyChar);
                    JObject jObject = JObject.Parse(json);

                    try
                    {
                        adPrice = Functions.ConvertPrice(jObject["price"]!.ToString(), "OMR");
                    }
                    catch
                    {
                        adPrice = "Не указана";
                    }

                    adTitle = jObject["ad_title"]!.ToString();
                    sellerType = jObject["seller_type"]!.ToString();

                    if(sellerType == "individual")
                    {
                        sellerType = "Частное лицо";
                    }
                    else
                    {
                        sellerType = "Бизнесс аккаунт";
                    }
                    break;
                }
            }

            if(Functions.CheckSellerType(userSellerType!, sellerType)){ }else{ return "NextAdvertisement"; }

            foreach (HtmlNode script in scripts)
            {
                if(script.InnerHtml.Contains("window.state = "))
                {
                    string json = script.InnerHtml.Split("window.state = ")[1].Split("\n")[0];
                    char[] MyChar = {';',')'};
                    json = json.TrimEnd(MyChar);
                    JObject jObject = JObject.Parse(json);

                    var sellerProfile = jObject["sellerProfile"]!;
                    string sellerId = sellerProfile["data"]!["externalID"]!.ToString();
                    sellerLink = $"https://www.olx.com.om/en/profile/{sellerId}";
                    
                    sellerName = sellerProfile["data"]!["name"]!.ToString();
                    sellerRegDate = Convert.ToDateTime(sellerProfile["data"]!["createdAt"]!.ToString());
                    break;
                }
            }

            if(Functions.CheckSellerRegDate(userSellerRegDate!, sellerRegDate)){ }else{ return "NextAdvertisement"; }

            try
            {
                HtmlDocument sellerDocument = web.Load(sellerLink);
                
                sellerTotalAds = Functions.LeaveOnlyNumbers(sellerDocument.DocumentNode.SelectSingleNode("//span[@class=\"_34a7409b _6a44af43 _2e82a662\"]").InnerText);
            }
            catch
            {
                sellerTotalAds = 1;
            }

            if(Functions.CheckSellerTotalAds(userSellerTotalAds!, sellerTotalAds)){ }else{ return "NextAdvertisement"; }

            try
            {
                adDescription = document.DocumentNode.SelectSingleNode("//div[@class=\"_0f86855a\"]//span").InnerText;
            }
            catch
            {
                adDescription = "Не указано";
            }

            try
            {
                adLocation = document.DocumentNode.SelectSingleNode("//span[@aria-label=\"Location\"]").InnerText;
            }
            catch
            {
                adLocation = "Не указано";
            }

            try
            {
                adImage = document.DocumentNode.SelectSingleNode("//img[@role=\"presentation\"]").GetAttributeValue("src", "").Replace("240x180.jpeg", "400x300.jpeg");
            }
            catch
            {
                adImage = errorImageUri;
            }
            
            Console.WriteLine(adTitle);
            Console.WriteLine(adPrice);
            Console.WriteLine(adLocation);
            // Console.WriteLine(adImage);
            Console.WriteLine(adRegDate);
            Console.WriteLine(sellerPhoneNumber);
            Console.WriteLine(sellerName);
            Console.WriteLine(sellerTotalAds);
            Console.WriteLine(sellerRegDate);
            Functions.AddToBlacklist(userId, userPlatform!, adLink, sellerLink, sellerPhoneNumber);

            await SendLogToTg(botClient, userId, adLink, adTitle, adDescription, adPrice, adLocation, adImage, adRegDate, sellerPhoneNumber, sellerName, sellerLink, sellerTotalAds, sellerRegDate, sellerType);
            return "NextAdvertisement";
        }


        static async Task SendLogToTg(ITelegramBotClient botClient, long userId, string adLink, string adTitle, string adDescription, string adPrice, string adLocation, string adImage, DateTime adRegDate, string sellerPhoneNumber, string sellerName, string sellerLink, int sellerTotalAds, DateTime sellerRegDate, string sellerType)
        {
            string whatsappText = LinkGenerator.GenerateWhatsAppText(DB.GetWhatsappText(userId), adLink, adTitle, adPrice, adLocation, sellerName);

            string adInfo = $"<b>📦 Название: </b><code>{adTitle}</code>\n<b>📞 Номер: </b><code>{sellerPhoneNumber}</code>\n<b>💲 Цена: </b>{adPrice}\n<b>🧔🏻 Продавец: </b><a href=\"{sellerLink}\">{sellerName}</a>\n\n<b>📅 Добавлено: </b><b>{adRegDate.ToString().Split(' ')[0]}</b> <code>{adRegDate.ToString().Split(' ')[1]}</code>\n<b>📝 Количество объявлений: </b><b>{sellerTotalAds}</b>\n<b>📆 Дата регистрации: </b><b>{sellerRegDate.ToString("dd.MM.yyyy")}</b>\n\n<b>🖨 Описание: </b>{adDescription}\n\n<a href=\"{adLink}\">Переход на объявление</a>\n<a href=\"https://api.whatsapp.com/send?phone={sellerPhoneNumber}&text={whatsappText}\">Написать WhatsApp</a>";


            try
            {
                try
                {
                    await botClient.SendPhotoAsync(
                        chatId: userId,
                        photo: adImage,
                        caption: adInfo,
                        parseMode: ParseMode.Html
                    );
                }
                catch(Exception e)
                {
                    Console.WriteLine(e);
                    await botClient.SendPhotoAsync(
                        chatId: userId,
                        photo: errorImageUri,
                        caption: adInfo,
                        parseMode: ParseMode.Html
                    );
                }
            }
            catch(Exception e)
            { 
                Console.WriteLine(e);
                return; 
            }

            System.Threading.Thread.Sleep(2000);
        }
       


        static async Task<String> GetPhoneNumber(string adId)
        {
            try
            {
                string link = $"https://www.olx.com.om/api/listing/{adId}/contactInfo/";
                    
                HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", userAgent);
                client.DefaultRequestHeaders.Add("accept", "application/json");

                HttpResponseMessage response = await client.GetAsync(link);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                JObject jObject = JObject.Parse(json);
                string phoneNumber = Functions.ConvertPhone(jObject["mobile"]!.ToString());

                if(String.IsNullOrEmpty(phoneNumber))
                {
                    return "null";
                }

                if(!phoneNumber.Contains("+"))
                {
                    if(phoneNumber.Substring(0, 3) == "968")
                    {
                        phoneNumber = $"+{phoneNumber}";
                    }
                    else
                    {
                        phoneNumber = $"+968{phoneNumber}";
                    }
                }

                return phoneNumber;
            }
            catch
            {
                return "null";
            }
        }

        static string GenerateLink(int page)
        {
            string newLink = "";

            if(parserCategory == "all-categories")
            {                    
                newLink = $"https://www.olx.com.om/en/ads/?page={page}";
                Console.WriteLine(newLink);
                return newLink;
            }
            else
            {
                if(userLink!.Contains("https://www.olx.com.om/"))
                {
                    if(userLink[^1] == '/')
                    {
                        newLink = userLink + "?page=" + page.ToString();
                    }
                    else
                    {
                        newLink = userLink + "/?page=" + page.ToString();
                    }
                    return newLink;
                }
                else
                {
                    newLink = $"https://www.olx.com.om/ads/q-{userLink}/?page={page}";
                    return newLink;
                }
            }            
        }
    }
}