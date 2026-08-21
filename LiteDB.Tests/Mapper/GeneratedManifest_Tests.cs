using System;
using System.Collections.Generic;
using FluentAssertions;
using LiteDB.AOT;
using Xunit;

namespace LiteDB.Tests.Generator
{
    public class GeneratedManifest_Tests
    {
        [Fact]
        public void Should_map_with_generated_contract()
        {
            var mapper = new BsonMapper().UseGeneratedMappers(new LiteDbGeneratedMapperProvider());
            var document = mapper.ToDocument(new Customer { Id = 7, Name = "Ada", Email = "ada@example.test", Transient = "skip" });

            document.Keys.Should().BeEquivalentTo("_id", "name", "email");
            document["_id"].AsInt32.Should().Be(7);
            document["name"].AsString.Should().Be("Ada");
            document["email"].AsString.Should().Be("ada@example.test");
            document.ContainsKey("Transient").Should().BeFalse();

            var restored = mapper.Deserialize<Customer>(document);
            restored.Id.Should().Be(7);
            restored.Name.Should().Be("Ada");
            restored.Email.Should().Be("ada@example.test");

            mapper.GetExpression<Customer, bool>(customer => customer.Name == "Ada")
                .Should().NotBeNull();
        }

        [Fact]
        public void Should_reject_unregistered_type_in_strict_mode()
        {
            var mapper = new BsonMapper().UseGeneratedMappers(new LiteDbGeneratedMapperProvider());

            var action = () => mapper.ToDocument(new Unregistered { Value = "no contract" });

            action.Should().Throw<LiteException>()
                .Which.Message.Should().Contain("Unregistered");
        }

        [Fact]
        public void Should_use_generated_parameterized_constructor()
        {
            var mapper = new BsonMapper().UseGeneratedMappers(new LiteDbGeneratedMapperProvider());
            var document = new BsonDocument
            {
                ["_id"] = 11,
                ["name"] = "Grace"
            };

            var restored = mapper.Deserialize<ImmutableCustomer>(document);

            restored.Id.Should().Be(11);
            restored.Name.Should().Be("Grace");
        }

        [Fact]
        public void Should_map_reachable_collection_element_with_generated_contract()
        {
            var mapper = new BsonMapper().UseGeneratedMappers(new LiteDbGeneratedMapperProvider());
            var customer = new CustomerWithOrders
            {
                Orders = new List<Order>
                {
                    new Order { Number = "A-17" }
                }
            };

            var document = mapper.ToDocument(customer);
            var restored = mapper.Deserialize<CustomerWithOrders>(document);

            document["orders"].AsArray[0].AsDocument["number"].AsString.Should().Be("A-17");
            restored.Orders.Should().ContainSingle().Which.Number.Should().Be("A-17");
        }

    }

    [LiteEntity]
    public sealed class Customer
    {
        [BsonId]
        public int Id { get; set; }

        [BsonField("name")]
        public string Name { get; set; }

        [BsonField("email")]
        public string Email { get; set; }

        [BsonIgnore]
        public string Transient { get; set; }
    }

    public sealed class Unregistered
    {
        public string Value { get; set; }
    }

    [LiteEntity]
    public sealed class ImmutableCustomer
    {
        [BsonId]
        public int Id { get; }

        [BsonField("name")]
        public string Name { get; }

        public ImmutableCustomer(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    [LiteEntity]
    public sealed class CustomerWithOrders
    {
        public List<Order> Orders { get; set; }
    }

    public sealed class Order
    {
        public string Number { get; set; }
    }

}
