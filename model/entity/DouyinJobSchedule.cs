using System;
using SqlSugar;

namespace dy.net.model.entity
{
    [SugarTable("dy_job_schedule")]
    public class DouyinJobSchedule
    {
        [SugarColumn(IsPrimaryKey = true, Length = 50)]
        public string Type { get; set; }
        [SugarColumn(Length = 20)]
        public string ScheduleType { get; set; }
        [SugarColumn(Length = 200)]
        public string Expression { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
