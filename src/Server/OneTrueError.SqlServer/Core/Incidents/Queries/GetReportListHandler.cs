﻿using System.Linq;
using System.Threading.Tasks;
using DotNetCqs;
using Griffin.Container;
using Griffin.Data;
using Griffin.Data.Mapper;
using OneTrueError.Api.Core.Reports.Queries;
using OneTrueError.SqlServer.Tools;

namespace OneTrueError.SqlServer.Core.Incidents.Queries
{
    [Component]
    internal class GetReportListHandler : IQueryHandler<GetReportList, GetReportListResult>
    {
        private readonly IAdoNetUnitOfWork _unitOfWork;

        public GetReportListHandler(IAdoNetUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<GetReportListResult> ExecuteAsync(GetReportList query)
        {
            using (var cmd = _unitOfWork.CreateCommand())
            {
                var totalCount = 0;
                cmd.AddParameter("incidentId", query.IncidentId);
                if (query.PageNumber > 0)
                {
                    cmd.CommandText = "SELECT cast(count(Id) as int) FROM ErrorReports WHERE IncidentId = @incidentId";
                    totalCount = (int) cmd.ExecuteScalar();

                    cmd.CommandText =
                        "SELECT Id, Title, CreatedAtUtc, RemoteAddress, Exception FROM ErrorReports WHERE IncidentId = @incidentId ORDER BY Id DESC";

                    cmd.Paging(query.PageNumber, query.PageSize);
                }
                else
                {
                    cmd.CommandText =
                        "SELECT Id, Title, CreatedAtUtc, RemoteAddress, Exception FROM ErrorReports WHERE IncidentId = @incidentId ORDER BY Id DESC";
                }
                var items = await cmd.ToListAsync<GetReportListResultItem>();
                return new GetReportListResult(items.ToArray())
                {
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize,
                    TotalCount = totalCount
                };
            }
        }
    }
}