using System.Net;
using System.Text;

namespace AvyInReach.Tests;

public sealed class NwacProviderTests
{
    [Fact]
    public async Task ResolveRegionAsync_matches_known_alias_without_copilot()
    {
        var runner = new FakeProcessRunner("unused");
        var provider = new NwacProvider(new HttpClient(new NwacHandler()), runner);

        var region = await provider.ResolveRegionAsync("Mt. Hood", CancellationToken.None);

        Assert.NotNull(region);
        Assert.Equal("Mt Hood", region.DisplayName);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task ResolveRegionAsync_uses_copilot_for_location_lookup()
    {
        var runner = new FakeProcessRunner("Olympics");
        var provider = new NwacProvider(new HttpClient(new NwacHandler()), runner);

        var region = await provider.ResolveRegionAsync("Hurricane Ridge", CancellationToken.None);

        Assert.NotNull(region);
        Assert.Equal("Olympics", region.DisplayName);
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task GetForecastAsync_maps_nwac_forecast_and_weather_summary()
    {
        var provider = new NwacProvider(new HttpClient(new NwacHandler()), new FakeProcessRunner("unused"));
        var region = new ForecastRegion("nwac", "Olympics", "olympics", "Olympics", "https://nwac.us/avalanche-forecast/#/olympics");

        var forecast = await provider.GetForecastAsync(region, CancellationToken.None);

        Assert.NotNull(forecast);
        Assert.Equal("Northwest Avalanche Center", forecast.OwnerName);
        Assert.Equal(2, forecast.CurrentDangerRatings.BelowTreeline);
        Assert.Equal(3, forecast.CurrentDangerRatings.Treeline);
        Assert.Equal(4, forecast.CurrentDangerRatings.Alpine);
        Assert.Equal("5000 temps 27 / 19 F; 25 / 18 F. ridgeline winds W 0-10 mph; W 10-20 mph; WNW 5-15 mph; WNW 10-20 mph. forecast Scattered light snow showers; Partly cloudy. snow level 1000 ft; 0 ft; 0 ft; 0 ft", forecast.WeatherSummary);
        Assert.Equal("Special statement.", forecast.Message);
        Assert.Equal(2, forecast.Problems.Count);
        Assert.Equal("Wind Slab", forecast.Problems[0].Name);
        Assert.Equal(2, forecast.Problems[0].Likelihood);
        Assert.Equal("N-E-SE", forecast.Problems[0].AspectString());
    }

    private sealed class NwacHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null)
            {
                throw new InvalidOperationException("Missing request URI.");
            }

            if (request.RequestUri.AbsoluteUri.StartsWith("https://nwac.us/api/v2/avalanche-region-forecast", StringComparison.Ordinal))
            {
                const string json = """
                    {
                      "objects": [
                        {
                          "bottom_line_summary": "<p>Bottom line.</p>",
                          "day1_danger_elev_high": "High",
                          "day1_danger_elev_low": "Moderate",
                          "day1_danger_elev_middle": "Considerable",
                          "day1_date": "2026-03-13",
                          "day1_detailed_forecast": "<p>Detailed avalanche forecast.</p>",
                          "day1_warning": "none",
                          "day1_warning_text": "",
                          "optional_discussion": "<p>Optional discussion.</p>",
                          "publish_date": "2026-03-13T21:56:00Z",
                          "snowpack_discussion": "<p>Snowpack discussion.</p>",
                          "special_statement": "<p>Special statement.</p>",
                          "zones": [
                            {
                              "active": true,
                              "slug": "olympics",
                              "zone_abbrev": "Olympics",
                              "zone_name": "Olympics"
                            }
                          ],
                          "problems": [
                            {
                              "likelihood": "1-possible",
                              "maximum_size": "1-large",
                              "minimum_size": "0-small",
                              "order": 1,
                              "problem_description": "<p>Problem one.</p>",
                              "problem_type": {
                                "name": "Wind Slab",
                                "risk_management_description": "<p>Risk management.</p>"
                              },
                              "octagon_low_east": true,
                              "octagon_low_north": true,
                              "octagon_low_northeast": false,
                              "octagon_low_northwest": false,
                              "octagon_low_south": false,
                              "octagon_low_southeast": true,
                              "octagon_low_southwest": false,
                              "octagon_low_west": false,
                              "octagon_mid_east": false,
                              "octagon_mid_north": false,
                              "octagon_mid_northeast": false,
                              "octagon_mid_northwest": false,
                              "octagon_mid_south": false,
                              "octagon_mid_southeast": false,
                              "octagon_mid_southwest": false,
                              "octagon_mid_west": false,
                              "octagon_high_east": false,
                              "octagon_high_north": false,
                              "octagon_high_northeast": false,
                              "octagon_high_northwest": false,
                              "octagon_high_south": false,
                              "octagon_high_southeast": false,
                              "octagon_high_southwest": false,
                              "octagon_high_west": false
                            },
                            {
                              "likelihood": "2-likely",
                              "maximum_size": "2-very-large",
                              "minimum_size": "1-large",
                              "order": 2,
                              "problem_description": "<p>Problem two.</p>",
                              "problem_type": {
                                "name": "Storm Slab",
                                "risk_management_description": "<p>Risk management two.</p>"
                              },
                              "octagon_low_east": false,
                              "octagon_low_north": false,
                              "octagon_low_northeast": false,
                              "octagon_low_northwest": false,
                              "octagon_low_south": false,
                              "octagon_low_southeast": false,
                              "octagon_low_southwest": false,
                              "octagon_low_west": false,
                              "octagon_mid_east": true,
                              "octagon_mid_north": true,
                              "octagon_mid_northeast": true,
                              "octagon_mid_northwest": true,
                              "octagon_mid_south": true,
                              "octagon_mid_southeast": true,
                              "octagon_mid_southwest": true,
                              "octagon_mid_west": true,
                              "octagon_high_east": true,
                              "octagon_high_north": true,
                              "octagon_high_northeast": true,
                              "octagon_high_northwest": true,
                              "octagon_high_south": true,
                              "octagon_high_southeast": true,
                              "octagon_high_southwest": true,
                              "octagon_high_west": true
                            }
                          ]
                        }
                      ]
                    }
                    """;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }

            if (request.RequestUri.AbsoluteUri.StartsWith("https://nwac.us/weather-forecast-summary/", StringComparison.Ordinal))
            {
                const string html = """
                    <div class="weather-summary">
                      <div class="issued">
                        Issued on 2:56 PM PDT Friday, March 13, 2026
                              by Robert Hahn
                      </div>
                      <table class="nac-html-table nac-table">
                        <tbody>
                          <tr>
                            <th>5000' Temperatures (Max / Min)</th>
                            <td colspan="2">27 / 19 F</td>
                            <td colspan="2">25 / 18 F</td>
                          </tr>
                          <tr>
                            <th>Snow Level</th>
                            <td>1000 ft</td>
                            <td>0 ft</td>
                            <td>0 ft</td>
                            <td>0 ft</td>
                          </tr>
                          <tr>
                            <th>Ridgeline Winds</th>
                            <td>W 0-10 mph</td>
                            <td>W 10-20 mph</td>
                            <td>WNW 5-15 mph</td>
                            <td>WNW 10-20 mph</td>
                          </tr>
                          <tr>
                            <th>Weather Forecast</th>
                            <td colspan="2">Scattered light snow showers.</td>
                            <td colspan="2">Partly cloudy.</td>
                          </tr>
                        </tbody>
                      </table>
                    </div>
                    """;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(html, Encoding.UTF8, "text/html"),
                });
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
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
