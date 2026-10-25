using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.Domain.Tests.Support;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Catalog;

public sealed class ProjectTests
{
    private readonly Category _category = Make.Category();

    [Fact]
    [Rule("D-06")]
    public void A_complete_draft_with_an_open_amount_can_be_published()
    {
        var cover = Make.Image();
        var project = Project.CreateDraft(Make.Details(), _category, cover, Make.Now);

        project.Publish(_category, cover, Make.Now).IsSuccess.ShouldBeTrue();

        project.Status.ShouldBe(ProjectStatus.Published);
        project.PublishedAt.ShouldBe(Make.Now);
        project.DomainEvents.OfType<CatalogChanged>().Select(e => e.Change).ShouldBe(["created", "published"]);
    }

    [Fact]
    [Rule("D-06")]
    public void Publishing_lists_every_missing_requirement()
    {
        var project = Project.CreateDraft(Make.Details(openAmount: false, summary: null, body: " "), _category, null, Make.Now);

        var result = project.Publish(null, null, Make.Now);

        result.Error.Code.ShouldBe(ErrorCodes.ProjectPublishIncomplete);
        ((IEnumerable<string>)result.Error.Extensions!["missing"]!).ShouldBe(["summaryAr", "bodyAr", "cover", "category", "amount"], ignoreOrder: true);
        project.Status.ShouldBe(ProjectStatus.Draft);
    }

    [Fact]
    [Rule("D-06")]
    public void A_pdf_is_not_a_cover()
    {
        var pdf = Make.Pdf();

        Should.Throw<DomainException>(() => Project.CreateDraft(Make.Details(), _category, pdf, Make.Now)).Code.ShouldBe("media.image_required");
        Make.Draft(_category).MissingForPublish(_category, pdf).ShouldContain("cover");
    }

    [Fact]
    [Rule("D-06")]
    public void Options_only_projects_need_an_active_option()
    {
        var cover = Make.Image();
        var project = Project.CreateDraft(Make.Details(openAmount: false), _category, cover, Make.Now);
        project.ReplaceOptions([new ProjectOptionInput(null, "يوم", null, new Money(100m), 1, IsActive: false)], new HashSet<Guid>(), Make.Now);

        project.Publish(_category, cover, Make.Now).Error.Code.ShouldBe(ErrorCodes.ProjectPublishIncomplete);

        project.ReplaceOptions([new ProjectOptionInput(null, "يوم", null, new Money(100m), 1, IsActive: true)], new HashSet<Guid>(), Make.Now);
        project.Publish(_category, cover, Make.Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    [Rule("D-06")]
    public void A_published_project_cannot_lose_its_last_way_to_give()
    {
        var project = Make.Published(_category, openAmount: false, null, 25m);

        project.ReplaceOptions([], new HashSet<Guid>(), Make.Now).Error.Code.ShouldBe(ErrorCodes.ProjectPublishIncomplete);
        project.Update(Make.Details(project.Slug, openAmount: false, summary: null), _category, Make.Image(), Make.Now)
            .Error.Code.ShouldBe(ErrorCodes.ProjectPublishIncomplete);
        project.Options.Count.ShouldBe(1);
    }

    [Theory]
    [Rule("D-07")]
    [InlineData(ProjectStatus.Draft, ProjectStatus.Published, true)]
    [InlineData(ProjectStatus.Published, ProjectStatus.Archived, true)]
    [InlineData(ProjectStatus.Archived, ProjectStatus.Published, true)]
    [InlineData(ProjectStatus.Draft, ProjectStatus.Archived, false)]
    [InlineData(ProjectStatus.Published, ProjectStatus.Published, false)]
    [InlineData(ProjectStatus.Archived, ProjectStatus.Archived, false)]
    public void Only_the_allowed_status_changes_succeed(ProjectStatus from, ProjectStatus to, bool allowed)
    {
        var cover = Make.Image();
        var project = Project.CreateDraft(Make.Details(), _category, cover, Make.Now);
        if (from != ProjectStatus.Draft)
        {
            project.Publish(_category, cover, Make.Now);
        }

        if (from == ProjectStatus.Archived)
        {
            project.Archive(Make.Now);
        }

        project.Status.ShouldBe(from);
        var result = to == ProjectStatus.Published ? project.Publish(_category, cover, Make.Now) : project.Archive(Make.Now);

        result.IsSuccess.ShouldBe(allowed);
        project.Status.ShouldBe(allowed ? to : from);
        if (!allowed)
        {
            result.Error.Code.ShouldBe(ErrorCodes.ProjectInvalidTransition);
        }
    }

    [Fact]
    [Rule("D-08")]
    public void A_published_project_keeps_its_old_slug_for_redirects()
    {
        var project = Make.Published(_category, true, null);
        var old = project.Slug;

        project.Update(Make.Details("new-slug"), _category, Make.Image(), Make.Now).IsSuccess.ShouldBeTrue();

        project.Slug.ShouldBe("new-slug");
        project.SlugHistory.Select(h => h.OldSlug).ShouldBe([old]);
    }

    [Fact]
    [Rule("D-08")]
    public void A_draft_renames_without_history_and_taking_back_an_old_slug_removes_it()
    {
        var draft = Make.Draft(_category);
        draft.Update(Make.Details("draft-renamed"), _category, Make.Image(), Make.Now);
        draft.SlugHistory.ShouldBeEmpty();

        var project = Make.Published(_category, true, null);
        var first = project.Slug;
        project.Update(Make.Details("second"), _category, Make.Image(), Make.Now);
        project.Update(Make.Details(first), _category, Make.Image(), Make.Now);

        project.Slug.ShouldBe(first);
        project.SlugHistory.Select(h => h.OldSlug).ShouldBe(["second"]);
    }

    [Theory]
    [Rule("D-08")]
    [InlineData("a")]
    [InlineData("Upper")]
    [InlineData("space slug")]
    [InlineData("عربي")]
    [InlineData("slug_underscore")]
    public void Slugs_follow_the_pattern(string slug)
    {
        Slug.IsValidProject(slug).ShouldBeFalse();
        Should.Throw<DomainException>(() => Project.CreateDraft(Make.Details(slug), _category, null, Make.Now));
    }

    [Fact]
    [Rule("D-08")]
    public void Slug_lengths_are_2_to_80_for_projects_and_2_to_60_for_categories()
    {
        Slug.IsValidProject("ab").ShouldBeTrue();
        Slug.IsValidProject(new string('a', 80)).ShouldBeTrue();
        Slug.IsValidProject(new string('a', 81)).ShouldBeFalse();
        Slug.IsValidCategory(new string('a', 60)).ShouldBeTrue();
        Slug.IsValidCategory(new string('a', 61)).ShouldBeFalse();
    }

    [Fact]
    public void Replacing_options_deactivates_used_ones_and_deletes_the_others()
    {
        var project = Make.Published(_category, true, null, 25m, 100m, 500m);
        var used = project.Options[0];
        var unused = project.Options[1];
        var kept = project.Options[2];

        project.ReplaceOptions([new ProjectOptionInput(kept.Id, "أسبوع", "Week", new Money(450m), 1, true), new ProjectOptionInput(null, "شهر", null, new Money(2000m), 2, true)],
            new HashSet<Guid> { used.Id }, Make.Now).IsSuccess.ShouldBeTrue();

        project.Options.ShouldContain(used);
        used.IsActive.ShouldBeFalse();
        project.Options.ShouldNotContain(unused);
        kept.Amount.ShouldBe(new Money(450m));
        kept.LabelAr.ShouldBe("أسبوع");
        project.Options.Count.ShouldBe(3);
        project.ActiveOption(used.Id).ShouldBeNull();
    }

    [Fact]
    public void An_input_with_an_existing_label_reuses_that_option()
    {
        var project = Make.Published(_category, true, null, 25m);
        var option = project.Options[0];

        project.ReplaceOptions([new ProjectOptionInput(null, option.LabelAr, null, new Money(30m), 1, true)], new HashSet<Guid>(), Make.Now);

        project.Options.ShouldHaveSingleItem().Id.ShouldBe(option.Id);
        option.Amount.ShouldBe(new Money(30m));
    }

    [Fact]
    public void Option_labels_are_unique_and_amounts_positive()
    {
        var project = Make.Draft(_category);

        Should.Throw<DomainException>(() => project.ReplaceOptions(
            [new ProjectOptionInput(null, "يوم", null, new Money(1m), 1, true), new ProjectOptionInput(null, "يوم", null, new Money(2m), 2, true)],
            new HashSet<Guid>(), Make.Now));
        Should.Throw<DomainException>(() => project.ReplaceOptions([new ProjectOptionInput(null, "يوم", null, Money.Zero, 1, true)], new HashSet<Guid>(), Make.Now));
    }

    [Fact]
    [Rule("D-03")]
    public void The_minimum_of_an_open_amount_is_the_project_one_else_the_setting()
    {
        Make.Draft(_category, min: new Money(50m)).MinimumOpenAmount(new Money(10m)).ShouldBe(new Money(50m));
        Make.Draft(_category).MinimumOpenAmount(new Money(10m)).ShouldBe(new Money(10m));
        Should.Throw<DomainException>(() => Make.Draft(_category, min: Money.Zero));
    }

    [Fact]
    public void Versions_are_compared_for_optimistic_updates()
    {
        var project = Make.Draft(_category);

        project.IsVersion(0).ShouldBeTrue();
        project.IsVersion(7).ShouldBeFalse();
    }
}
