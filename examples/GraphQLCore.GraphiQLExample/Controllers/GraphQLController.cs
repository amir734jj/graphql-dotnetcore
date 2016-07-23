﻿namespace GraphQLCore.GraphiQLExample.Controllers
{
    using Exceptions;
    using Microsoft.AspNetCore.Mvc;
    using Models;
    using Newtonsoft.Json;
    using System;
    using System.Dynamic;
    using System.Linq;
    using Type;

    [Route("api/[controller]")]
    public class GraphQLController : Controller
    {
        private IGraphQLSchema schema;

        public GraphQLController(IGraphQLSchema schema)
        {
            this.schema = schema;
        }

        [HttpPost]
        public JsonResult Post([FromBody] GraphiQLInput input)
        {
            try
            {
                return this.Json(
                    new
                    {
                        data = this.schema.Execute(input.Query, GetVariables(input))
                    }
                );
            }
            catch (GraphQLValidationException ex)
            {
                return this.Json(
                    new
                    {
                        errors = ex.Errors.Select(e => new { message = e.Message })
                    }
                );
            }
            catch (Exception ex)
            {
                return this.Json(
                    new
                    {
                        errors = new dynamic[] { new { message = ex.Message } }
                    }
                );
            }
        }

        private static dynamic GetVariables(GraphiQLInput input)
        {
            if (string.IsNullOrWhiteSpace(input.Variables))
                return new ExpandoObject();

            return JsonConvert.DeserializeObject<ExpandoObject>(input.Variables);
        }
    }
}