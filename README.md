# HttpClient problems

This repo is made to demonstrate a problem I experience when using `HttpClient` in heavy load environments. The use of a for loop and multiple tasks spun up is a bit contrieved, but it is meant to simulate e.g. multiple integration tests running against the same service. We experience this problem when running multiple
xUnit tests at once against a .NET Api, but the API itself is irrelevant, as the API side remains responsive the whole time while the client starts experiencing
problems.

## Unit tests

There is only one unit test file, but it is included in two projects, one .net core 2.0, and one desktop (.net 4.6.1), just to rule out that there are any
important differences between the two. Running the tests reveal that .net core has is more efficient, as the parallel runs of the calls are much faster under load there than in the .net 4.6.1k one.

## The Api

The API consists of two methods, `api/values` and `api/values/slow`. They both return an array of two strings, but the latter pauses for 200ms before responding. They are set up in VS to run on ports 58526 (http)  and 44398 (https). I use both, to see if there are any differences between the two protocols, but there doesn't seem to be any.

## The tests

The test run the two API calls a number of times each, over both http and https. Some tests run them in sequence, which is a lot slower, but always succeeds, and in parallel, starting N tasks, and `Task.WhenAll`-ing them at the end.
There is only one `HttpClient` instance per endpoint (one for http, one for https).

## Behavior

Running the tests, they start failing with `TaskCancelledException`s after a while when running multiple tests at the same time. How many simultaneous calls can go through, differ a bit between the fast and the slow API, and is probably hardware dependent, and varies a bit betwen runs. But it typically handles from 1,500 up to more than 10,000 simultaneous calls on the fast one, a bit fewer if I add custom headers, etc; and around 200 on the slow call (with 200ms artificial processing time on the server).

We see the same behaviour in our integration tests, but on much fewer connections. The calls there take much longer. If we run the tests again, separately, they always succeed. So the number of simultaneous calls are indeed important.

## Goal

The goal of this repo is to get some firm answers on how to use multiple concurrent `HttpClient` connections at once from C# (be it from integration tests, or from server-side in e.g. a REST API, a windows service, etc)

* What are the limits on the number of concurrent usages of a single `HttpClient`?
* How is it related to the time used on each `SendAsync` call?
* Other limits to know about?