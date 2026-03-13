using System.Net;
using System.Text;

namespace AvyInReach.Tests;

public sealed class AvalancheCanadaProviderTests
{
    [Fact]
    public async Task ResolveRegionAsync_returns_direct_match_without_copilot()
    {
        var runner = new FakeProcessRunner("South Columbia");
        var provider = new AvalancheCanadaProvider(new HttpClient(new MetadataHandler()), runner);

        var region = await provider.ResolveRegionAsync("South Columbia", CancellationToken.None);

        Assert.NotNull(region);
        Assert.Equal("South Columbia", region.DisplayName);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task ResolveRegionAsync_uses_copilot_for_location_lookup()
    {
        var runner = new FakeProcessRunner("South Columbia");
        var provider = new AvalancheCanadaProvider(new HttpClient(new MetadataHandler()), runner);

        var region = await provider.ResolveRegionAsync("Mount Lequereux", CancellationToken.None);

        Assert.NotNull(region);
        Assert.Equal("South Columbia", region.DisplayName);
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task ResolveRegionAsync_returns_null_when_copilot_response_is_not_a_candidate()
    {
        var runner = new FakeProcessRunner("Not A Region");
        var provider = new AvalancheCanadaProvider(new HttpClient(new MetadataHandler()), runner);

        var region = await provider.ResolveRegionAsync("Mount Lequereux", CancellationToken.None);

        Assert.Null(region);
    }

    [Fact]
    public async Task GetForecastAsync_maps_problem_likelihood_to_five_point_scale()
    {
        var provider = new AvalancheCanadaProvider(new HttpClient(new ProductHandler()), new FakeProcessRunner("unused"));
        var region = new ForecastRegion("avalanche-canada", "South Columbia", "south-columbia", "area-1", "https://example.com");

        var forecast = await provider.GetForecastAsync(region, CancellationToken.None);

        Assert.NotNull(forecast);
        Assert.Single(forecast.Problems);
        Assert.Equal(3, forecast.Problems[0].Likelihood);
    }

    private sealed class MetadataHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = """
                [
                  {
                    "product": {
                      "type": "avalancheforecast",
                      "title": "South Columbia",
                      "id": "prod-1",
                      "reportId": "south-columbia",
                      "slug": "south-columbia"
                    },
                    "area": {
                      "id": "area-1",
                      "name": "South Columbia"
                    },
                    "url": "https://avalanche.ca/forecasts/south-columbia"
                  },
                  {
                    "product": {
                      "type": "avalancheforecast",
                      "title": "North Rockies",
                      "id": "prod-2",
                      "reportId": "north-rockies",
                      "slug": "north-rockies"
                    },
                    "area": {
                      "id": "area-2",
                      "name": "North Rockies"
                    },
                    "url": "https://avalanche.ca/forecasts/north-rockies"
                  }
                ]
                """;

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
        }
    }

    private sealed class ProductHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = """
                [
                  {
                    "url": "https://avalanche.ca/forecasts/south-columbia",
                    "owner": {
                      "display": "Avalanche Canada"
                    },
                    "report": {
                      "id": "south-columbia",
                      "title": "South Columbia",
                      "forecaster": "Avalanche Canada",
                      "dateIssued": "2026-03-13T12:00:00Z",
                      "validUntil": "2026-03-13T23:00:00Z",
                      "timezone": "America/Vancouver",
                      "highlights": "<p>Highlights</p>",
                      "summaries": [
                        { "type": { "value": "avalanche-summary" }, "content": "<p>Avalanche summary</p>" },
                        { "type": { "value": "snowpack-summary" }, "content": "<p>Snowpack summary</p>" },
                        { "type": { "value": "weather-summary" }, "content": "<p>Weather summary</p>" }
                      ],
                      "dangerRatings": [
                        {
                          "ratings": {
                            "alp": { "rating": { "display": "3 - Considerable" } },
                            "tln": { "rating": { "display": "2 - Moderate" } },
                            "btl": { "rating": { "display": "1 - Low" } }
                          }
                        }
                      ],
                      "problems": [
                        {
                          "type": { "display": "Wind slab" },
                          "comment": "<p>Notes</p>",
                          "data": {
                            "elevations": [
                              { "value": "btl" },
                              { "value": "tln" },
                              { "value": "alp" }
                            ],
                            "aspects": [
                              { "value": "n" },
                              { "value": "e" }
                            ],
                            "likelihood": { "value": "likely", "display": "Likely" },
                            "expectedSize": { "min": "1.0", "max": "2.0" }
                          }
                        }
                      ],
                      "message": "Message"
                    }
                  }
                ]
                """;

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
        }
    }

    private sealed class FakeProcessRunner(string output) : IProcessRunner
    {
        public int CallCount { get; private set; }

        public Task<ProcessRunResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ProcessRunResult(0, output, string.Empty));
        }
    }
}
