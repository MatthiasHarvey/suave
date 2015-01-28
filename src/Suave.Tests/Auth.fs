﻿module Suave.Tests.Auth


open System
open System.Net
open System.Net.Http

open Fuchu

open Suave
open Suave.Logging
open Suave.Cookie
open Suave.Http
open Suave.Web
open Suave.Types
open Suave.Http.Successful
open Suave.Http.Applicatives
open Suave.Http.RequestErrors

open Suave.Testing

let run_with' = run_with { default_config with logger = Loggers.sane_defaults_for LogLevel.Debug }

type Assert with
  static member Null (msg : string, o : obj) =
    if o <> null then Tests.failtest msg
    else ()

  static member Contains (msg : string, f_expected : 'a -> bool, xs : seq<'a>) =
    if Seq.isEmpty xs then Tests.failtest "empty seq"
    match Seq.tryFind f_expected xs with
    | None -> Tests.failtest msg
    | Some v ->
      // printfn "found %A" v
      ()

let req_resp
  (methd : HttpMethod)
  (resource : string)
  (cookies : CookieContainer option)
  (f_request : HttpRequestMessage -> HttpRequestMessage)
  f_result
  ctx =

  let log = Suave.Log.info ctx.suave_config.logger "Suave.Tests" TraceHeader.empty
  log (sprintf "%A %s" methd resource)

  let default_timeout = TimeSpan.FromSeconds 5.

  use handler = mk_handler DecompressionMethods.None cookies
  use client = mk_client handler
  use request = mk_request methd resource "" None (endpoint_uri ctx.suave_config) |> f_request

  for h in request.Headers do
    log (sprintf "%s: %s" h.Key (String.Join(", ", h.Value)))

  // use -> let!!!
  let result = request |> send client default_timeout ctx
  f_result result

let set_connection_keep_alive (r : HttpRequestMessage) =
  r.Headers.ConnectionClose <- Nullable(false)
  r

/// Test a request by looking at the cookies alone.
let req_cookies cookies ctx methd resource f_req =
  req_resp methd resource (Some cookies)
           set_connection_keep_alive
           f_req
           ctx

let cookies suave_config (container : CookieContainer) =
  container.GetCookies(endpoint_uri suave_config)

let interaction ctx f_ctx = with_context f_ctx ctx

[<Tests>]
let tests =
  testList "auth tests" [
    testCase "baseline, no auth cookie" <| fun _ ->
      let ctx = run_with' (OK "ACK")
      let cookies = ctx |> req_cookies' HttpMethod.GET "/"  None
      Assert.Null("should not have auth cookie", cookies.[Auth.SessionAuthCookie])
      
    testCase "can set cookie" <| fun _ ->
      let ctx = run_with' (Auth.authenticated Session false >>= OK "ACK")
      let cookies = ctx |> req_cookies' HttpMethod.GET "/"  None
      Assert.NotNull("should have auth cookie", cookies.[Auth.SessionAuthCookie])

    testCase "can access authenticated contents" <| fun _ ->
      // given
      let ctx =
        run_with' (
          choose [
            url "/" >>= OK "root"
            url "/auth" >>= Auth.authenticated Session false >>= OK "authed"
            url "/protected"
              >>= Auth.authenticate Session false
                                    (fun () ->
                                      Choice2Of2(FORBIDDEN "please authenticate"))
                                    (fun _ -> Choice2Of2(BAD_REQUEST "did you fiddle with our cipher text?"))
                                    (OK "You have reached the place of your dreams!")
            NOT_FOUND "arghhh"
            ])

      // mutability bonanza here:
      let container = CookieContainer()
      let interact methd resource =
        let response = req_cookies container ctx methd resource id
        // set the response cookies accordingly
        match response.Headers.TryGetValues("Set-Cookie") with
        | false, _ -> ()
        | true, values -> values |> Seq.iter (fun cookie -> container.SetCookies(endpoint_uri ctx.suave_config, cookie))
        response

      let cookies = cookies ctx.suave_config container

      // when
      interaction ctx <| fun _ ->
        use res = interact HttpMethod.GET "/"
        Assert.Equal("should allow root request", "root", content_string res)

        match cookies.[Auth.SessionAuthCookie] with
        | null -> ()
        | cookie -> Tests.failtestf "should not have auth cookie, but was %A" cookie

        use res' = interact HttpMethod.GET "/protected"
        Assert.Equal("should not have access to protected", "please authenticate", content_string res')
        Assert.Equal("code 403 FORBIDDEN", HttpStatusCode.Forbidden, status_code res')

        use res'' = interact HttpMethod.GET "/auth"
        Assert.Contains("after authentication", (fun (str : string) -> str.Contains("auth=")),
                                                res''.Headers.GetValues "Set-Cookie")
        Assert.Equal("after authentication", "authed", content_string res'')

        use res''' = interact HttpMethod.GET "/protected"
        Assert.Equal("should have access to protected", "You have reached the place of your dreams!", content_string res''')
        Assert.Equal("code 200 OK", HttpStatusCode.OK, status_code res''')
    ]