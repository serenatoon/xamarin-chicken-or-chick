﻿using System;
using System.Diagnostics.Contracts;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Plugin.Media;
using Plugin.Media.Abstractions;
using Tabs.Model;
using Xamarin.Forms;
using Plugin.Geolocator;
using System.Globalization;

namespace Tabs
{
    public partial class CustomVision : ContentPage
    {
        public CustomVision()
        {
            InitializeComponent();
        }

        private async void loadCamera(object sender, EventArgs e)
        {
            await CrossMedia.Current.Initialize();

            if (!CrossMedia.Current.IsCameraAvailable || !CrossMedia.Current.IsTakePhotoSupported)
            {
                await DisplayAlert("No Camera", ":( No camera available.", "OK");
                return;
            }

            MediaFile file = await CrossMedia.Current.TakePhotoAsync(new StoreCameraMediaOptions
            {
                PhotoSize = PhotoSize.Medium,
                Directory = "Sample",
                Name = $"{DateTime.UtcNow}.jpg"
            });

            if (file == null)
                return;

            image.Source = ImageSource.FromStream(() =>
            {
                return file.GetStream();
            });

            TagLabel.Text = "Analysing.......";
            await postLocationAsync(); // post location 
            await MakePredictionRequest(file);
        }

        async Task postLocationAsync()
        {

            var locator = CrossGeolocator.Current;
            locator.DesiredAccuracy = 50;

            var position = await locator.GetPositionAsync(TimeSpan.FromSeconds(10)); // convert from int to timespan, '1000' doesn't seem to work 

            IsChickenModel model = new IsChickenModel()
            {
                Longitude = (float)position.Longitude,
                Latitude = (float)position.Latitude,
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm",
                                       CultureInfo.InvariantCulture)

            };

            await AzureManager.AzureManagerInstance.PostChickenInformation(model);
        }

        static byte[] GetImageAsByteArray(MediaFile file)
        {
            var stream = file.GetStream();
            BinaryReader binaryReader = new BinaryReader(stream);
            return binaryReader.ReadBytes((int)stream.Length);
        }

        async Task MakePredictionRequest(MediaFile file)
        {
            Contract.Ensures(Contract.Result<Task>() != null);
            var client = new HttpClient();

            client.DefaultRequestHeaders.Add("Prediction-Key", "2596b2fb91e34f07b738da9bd906e5ef");

            string url = "https://southcentralus.api.cognitive.microsoft.com/customvision/v1.0/Prediction/ad5a477e-3b43-42c8-95f7-e7a25e220eca/image?iterationId=67106974-19ae-498c-aff2-40297f6405d0";

            HttpResponseMessage response;

            byte[] byteData = GetImageAsByteArray(file);

            using (var content = new ByteArrayContent(byteData))
            {

                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response = await client.PostAsync(url, content);


                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();

                    EvaluationModel responseModel = JsonConvert.DeserializeObject<EvaluationModel>(responseString);

                    TagLabel.Text = "";
                    string type = ""; // to print what the photo is of
                    foreach (var prediction in responseModel.Predictions)
                    {
                        TagLabel.Text += "It is " + Math.Round(prediction.Probability * 100, 2) + "% likely to be a " + prediction.Tag + "\n"; // print probability of it being a chicken or a chick

                        // determine if it is a chicken or a chick 
                        // will overwrite the default "cannot determine what that is" if exceeds threshold (0.5)
                        if (prediction.Probability > 0.5)
                        {
                            type = prediction.Tag; 
                        }
                    }

                    if (type == "")
                    {
                        TagLabel.Text += "\n Cannot determine what that is! \n";
                    }
                    else
                    {
                        TagLabel.Text += "\n Looks like it's a " + type + "!";
                    }
                }

                //Get rid of file once we have finished using it
                file.Dispose();
            }
        }
    }
}
