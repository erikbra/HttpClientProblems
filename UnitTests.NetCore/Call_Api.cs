using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable once CheckNamespace
namespace UnitTests
{
    // ReSharper disable once InconsistentNaming
    public class Call_Api : IDisposable
    {
        private const string FastRelativeUri = "api/values";
        private const string SlowRelativeUri = "api/values/slow";

        private readonly ITestOutputHelper _output;
        private readonly Uri _secureBaseAddress = new Uri("https://localhost:44398");
        private readonly Uri _httpBaseAddress = new Uri("http://localhost:58526");

        private readonly HttpClient _secureHttpClient;
        private readonly HttpClient _httpHttpClient;

        private const int MaxNumErrorsToReport = 1;
        private int _numErrors = 0;

        public Call_Api(ITestOutputHelper output)
        {
            ServicePointManager.Expect100Continue = false;

            _output = output;
            
            _secureHttpClient = new HttpClient() {BaseAddress = _secureBaseAddress};
            _httpHttpClient = new HttpClient() {BaseAddress = _httpBaseAddress};

        }

        [Fact]
        public async Task Fast_Once()
        {
            var res = await CallApi(_secureHttpClient, FastRelativeUri);

            res.Length.Should().Be(2);
            res.First().Should().Be("value1");
            res.Last().Should().Be("value2");
        }

        [Fact]
        public async Task Slow_Once()
        {
            var res = await CallApi(_secureHttpClient, SlowRelativeUri);

            res.Length.Should().Be(2);
            res.First().Should().StartWith("value1");
            res.Last().Should().StartWith("value2");
        }

        [Theory()]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(10000)]
        public async Task Fast_Multiple_Times_In_A_Row(int numTimes)
        {
            var res = await CallMultpleTimesInARow(_secureHttpClient, FastRelativeUri, numTimes);
            AssertNoFailedCalls(res);
        }

        [Theory()]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(1500)]
        [InlineData(2000)]
        [InlineData(10000)]
        public async Task Https_Fast_Multiple_Times_In_Parallel(int numTimes)
        {
            var res = await CallMultipleTimesInParallel(_secureHttpClient, FastRelativeUri, numTimes);
            AssertNoFailedCalls(res);
        }


        [Theory()]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(1500)]
        [InlineData(2000)]
        [InlineData(10000)]
        public async Task Http_Fast_Multiple_Times_In_Parallel(int numTimes)
        {
            var res = await CallMultipleTimesInParallel(_httpHttpClient, FastRelativeUri, numTimes);
            AssertNoFailedCalls(res);
        }

        [Theory()]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        //[InlineData(1000)]
        //[InlineData(10000)]
        public async Task Slow_Multiple_Times_In_A_Row(int numTimes)
        {
            var res = await CallMultpleTimesInARow(_secureHttpClient, SlowRelativeUri, numTimes);
            AssertNoFailedCalls(res);
        }

        [Theory()]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(200)]
        [InlineData(400)]
        [InlineData(1000)]
        [InlineData(1500)]
        [InlineData(10000)]
        public async Task Slow_Multiple_Times_In_Parallel(int numTimes)
        {
            var res = await CallMultipleTimesInParallel(_secureHttpClient, SlowRelativeUri, numTimes);
            AssertNoFailedCalls(res);
        }

        private class CallResultSet
        {
            public long WallClockTicks { get; set; }
            public CallResult[] CallResults { get; set; }
        }

        private class CallResult
        {
            public long ElapsedTicks { get; set; }
            public bool Successful { get; set; }
        }

        private async Task<CallResultSet> CallMultipleTimesInParallel(HttpClient httpClient, string relativeUri,
            int numTimes)
        {
            var sw = Stopwatch.StartNew();

            var tasks = new List<Task<CallResult>>();
            for (var i = 0; i < numTimes; i++)
            {
                tasks.Add(Measure(() => CallApi(httpClient, relativeUri)));
            }
            var results = await Task.WhenAll(tasks);

            sw.Stop();

            return new CallResultSet()
            {
                WallClockTicks = sw.ElapsedTicks,
                CallResults = results
            };
        }

        private async Task<CallResultSet> CallMultpleTimesInARow(HttpClient httpClient, string relativeUri,
            int numTimes)
        {
            var sw = Stopwatch.StartNew();

            var results = new CallResult[numTimes];
            for (var i = 0; i < numTimes; i++)
            {
                results[i] = await Measure(() => CallApi(httpClient, relativeUri));
            }

            sw.Stop();
            return new CallResultSet()
            {
                WallClockTicks = sw.ElapsedTicks,
                CallResults = results
            };
        }

        private void AssertNoFailedCalls(CallResultSet results)
        {
            var callResults = results.CallResults;
            var length = callResults.Length;
            var failed = new List<int>(length);

            for (var i = 0; i < length; i++)
            {
                if (!callResults[i].Successful)
                {
                    failed.Add(i);
                }
            }

            ReportResults(results);
            failed.Should().BeEmpty("there should be no failed calls");
        }


        private async Task<string[]> CallApi(HttpClient httpClient, string relativeUri)
        {
            var resp = await httpClient.GetAsync(relativeUri);
            resp.EnsureSuccessStatusCode();
            HttpContent content = resp.Content;
            var json = await content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<string[]>(json);
        }

        private async Task<CallResult> Measure(Func<Task> task)
        {
            Stopwatch sw = Stopwatch.StartNew();
            var successful = false;

            try
            {
                await task();
                successful = true;
            }
            catch (Exception e)
            {
                if (++_numErrors <= MaxNumErrorsToReport)
                {
                    _output.WriteLine("" + e);
                }
                // ignored
            }
            sw.Stop();

            return new CallResult()
            {
                ElapsedTicks = sw.ElapsedTicks,
                Successful = successful
            };
        }

        private void ReportResults(CallResultSet results)
        {
            var callResults = results.CallResults;
            var totalElapsed = callResults.Sum(r => r.ElapsedTicks);
            var numCalls = callResults.Length;
            var numFailed = callResults.Count(r => !r.Successful);

            var average = TimeSpan.FromTicks(totalElapsed / numCalls);

            var wallClockTime = TimeSpan.FromTicks(results.WallClockTicks);

            _output.WriteLine(
                $"Time elapsed: {wallClockTime.TotalMilliseconds}ms. Called {numCalls}, failed {numFailed}. Avg call time: {average.TotalMilliseconds}ms");
        }

        public void Dispose()
        {
            _secureHttpClient?.Dispose();
        }
    }
}