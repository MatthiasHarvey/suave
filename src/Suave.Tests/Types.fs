﻿module Suave.Tests.Types

open System
open System.Net

open Fuchu

open Suave.Sockets
open Suave.Types

[<Tests>]
let socket_binding =
  testList "SocketBinding" [
    testCase "on IPv6" <| fun _ ->
      Assert.Equal("", "[::]:3050", (SocketBinding.mk IPAddress.IPv6Any 3050us).ToString())
    ]

[<Tests>]
let http_binding =
  testList "HttpBinding" [
    testCase "assumption about IPV4 loopback" <| fun _ ->
      Assert.Equal("", "127.0.0.1", IPAddress.Loopback.ToString())

    testCase "assumption about IPV6 loopback" <| fun _ ->
      Assert.Equal("", "::1", IPAddress.IPv6Loopback.ToString())

    testCase "IPv6 uri" <| fun _ ->
      let binding = HttpBinding.mk HTTP IPAddress.IPv6Loopback 8084us
      Assert.Equal("uri", "http://[::1]:8084/a?b=2",
                   binding.uri "/a" "b=2" |> sprintf "%O")

    testCase "IPv6 uri 2" <| fun _ ->
      let binding = HttpBinding.mk HTTP IPAddress.IPv6Loopback 80us
      Assert.Equal("uri", "http://[::1]/a?b=2",
                   binding.uri "/a" "b=2" |> sprintf "%O")

    testCase "IPv4 uri" <| fun _ ->
      let binding = HttpBinding.mk HTTP IPAddress.Loopback 8084us
      Assert.Equal("uri", "http://127.0.0.1:8084/a?b=2",
                   binding.uri "/a" "b=2" |> sprintf "%O")

    testCase "IPv4 uri 2" <| fun _ ->
      let binding = HttpBinding.mk HTTP IPAddress.Loopback 80us
      Assert.Equal("uri", "http://127.0.0.1/a?b=2",
                   binding.uri "/a" "b=2" |> sprintf "%O")

    testCase "IPv4 uri 3" <| fun _ ->
      let binding = HttpBinding.mk HTTP IPAddress.Loopback 80us
      Assert.Equal("uri", "http://127.0.0.1/",
                   binding.uri "/" "" |> sprintf "%O")
    ]