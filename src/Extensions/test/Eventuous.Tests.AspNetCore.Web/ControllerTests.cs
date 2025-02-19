// Copyright (C) Ubiquitous AS. All rights reserved
// Licensed under the Apache License, Version 2.0.

using System.Net;
using Eventuous.AspNetCore.Web;
using Eventuous.Sut.App;
using Eventuous.Sut.AspNetCore;
using Eventuous.Sut.Domain;
using Eventuous.TestHelpers;
using Eventuous.Tests.AspNetCore.Web.Fixture;
using RestSharp;

namespace Eventuous.Tests.AspNetCore.Web;

public class ControllerTests : IDisposable {
    readonly ServerFixture       _fixture;
    readonly TestEventListener   _listener;

    public ControllerTests(ITestOutputHelper output) {
        var commandMap = new MessageMap()
            .Add<BookingApi.RegisterPaymentHttp, Commands.RecordPayment>(
                x => new Commands.RecordPayment(
                    new BookingId(x.BookingId),
                    x.PaymentId,
                    new Money(x.Amount),
                    x.PaidAt
                )
            );

        _fixture = new ServerFixture(
            services => {
                services.AddSingleton(commandMap);
                services.AddControllers();
            },
            app => app.MapControllers()
        );

        _listener = new TestEventListener(output);
    }

    [Fact]
    public async Task RecordPaymentUsingMappedCommand() {
        using var client = _fixture.GetClient();

        var bookRoom = _fixture.GetBookRoom();

        var firstResponse = await client.PostJsonAsync("/book", bookRoom);

        var registerPayment = new BookingApi.RegisterPaymentHttp(
            bookRoom.BookingId,
            bookRoom.RoomId,
            100,
            DateTimeOffset.Now
        );

        var request  = new RestRequest("/v2/pay").AddJsonBody(registerPayment);
        var response = await client.ExecutePostAsync<OkResult>(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var expected = new BookingEvents.BookingFullyPaid(registerPayment.PaidAt);

        var events = await _fixture.ReadStream<Booking>(bookRoom.BookingId);
        var last   = events.LastOrDefault();
        last?.Payload.Should().BeEquivalentTo(expected);
    }

    public void Dispose() {
        _fixture.Dispose();
        _listener.Dispose();
    }
}