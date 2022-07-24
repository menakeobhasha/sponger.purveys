using Nml.Improve.Me.Dependencies;
using System;
using System.Linq;

namespace Nml.Improve.Me
{
    public class PdfApplicationDocumentGenerator : IApplicationDocumentGenerator
    {
        private readonly IConfiguration _configuration;
        private readonly IDataContext _dataContext;
        private readonly ILogger<PdfApplicationDocumentGenerator> _logger;
        private readonly IPathProvider _templatePathProvider;
        private readonly IPdfGenerator _pdfGenerator;
        private readonly IViewGenerator _viewGenerator;

        public PdfApplicationDocumentGenerator(
            IConfiguration configuration,
            IDataContext dataContext,
            ILogger<PdfApplicationDocumentGenerator> logger,
            IPathProvider templatePathProvider,
            IPdfGenerator pdfGenerator,
            IViewGenerator viewGenerator)
        {
            _configuration = configuration;
            _dataContext = dataContext;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pdfGenerator = pdfGenerator;
            _templatePathProvider = templatePathProvider ?? throw new ArgumentNullException("templatePathProvider");
            _viewGenerator = viewGenerator;
        }

        public byte[] Generate(Guid applicationId, string baseUri)
        {
            Application application = _dataContext.Applications.Single(app => app.Id == applicationId);

            if (application != null)
            {
                if (baseUri.EndsWith("/"))
                    baseUri = baseUri.Substring(baseUri.Length - 1);

                string view = string.Empty;
                string path = string.Empty;
                object viewModel = null;

                switch (application.State)
                {
                    case ApplicationState.Pending:
                        path = _templatePathProvider.Get("PendingApplication");
                        viewModel = new PendingApplicationViewModel
                        {
                            ReferenceNumber = application.ReferenceNumber,
                            State = application.State.ToDescription(),
                            FullName = $"{application.Person.FirstName} {application.Person.Surname}",
                            AppliedOn = application.Date,
                            SupportEmail = _configuration.SupportEmail,
                            Signature = _configuration.Signature
                        };
                        break;

                    case ApplicationState.Activated:
                        path = _templatePathProvider.Get("ActivatedApplication");
                        viewModel = new ActivatedApplicationViewModel
                        {
                            ReferenceNumber = application.ReferenceNumber,
                            State = application.State.ToDescription(),
                            FullName = $"{application.Person.FirstName} {application.Person.Surname}",
                            LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
                            PortfolioFunds = application.Products.SelectMany(p => p.Funds),
                            PortfolioTotalAmount = application.Products.SelectMany(p => p.Funds)
                                                           .Select(f => (f.Amount - f.Fees) * _configuration.TaxRate)
                                                           .Sum(),
                            AppliedOn = application.Date,
                            SupportEmail = _configuration.SupportEmail,
                            Signature = _configuration.Signature
                        };
                        break;

                    case ApplicationState.InReview:
                        path = _templatePathProvider.Get("InReviewApplication");
                        string inReviewMessage = "Your application has been placed in review" +
                                            application.CurrentReview.Reason switch
                                            {
                                                { } reason when reason.Contains("address") =>
                                                    " pending outstanding address verification for FICA purposes.",
                                                { } reason when reason.Contains("bank") =>
                                                    " pending outstanding bank account verification.",
                                                _ =>
                                                    " because of suspicious account behaviour. Please contact support ASAP."
                                            };
                        viewModel = new InReviewApplicationViewModel
                        {
                            ReferenceNumber = application.ReferenceNumber,
                            State = application.State.ToDescription(),
                            FullName = $"{application.Person.FirstName} {application.Person.Surname}",
                            LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
                            PortfolioFunds = application.Products.SelectMany(p => p.Funds),
                            PortfolioTotalAmount = application.Products.SelectMany(p => p.Funds)
                            .Select(f => (f.Amount - f.Fees) * _configuration.TaxRate)
                            .Sum(),
                            InReviewMessage = inReviewMessage,
                            InReviewInformation = application.CurrentReview,
                            AppliedOn = application.Date,
                            SupportEmail = _configuration.SupportEmail,
                            Signature = _configuration.Signature
                        };
                        break;

                    case ApplicationState.Closed:
                        _logger.LogWarning($"The application is in state '{application.State}' and no valid document can be generated for it.");
                        viewModel = string.Empty;
                        break;
                }

                view = _viewGenerator.GenerateFromPath(baseUri + path, viewModel);

                PdfOptions pdfOptions = new PdfOptions
                {
                    PageNumbers = PageNumbers.Numeric,
                    HeaderOptions = new HeaderOptions
                    {
                        HeaderRepeat = HeaderRepeat.FirstPageOnly,
                        HeaderHtml = PdfConstants.Header
                    }
                };

                return _pdfGenerator.GenerateFromHtml(view, pdfOptions).ToBytes();
            }
            else
            {
                _logger.LogWarning($"No application found for id '{applicationId}'");
                return new byte[0];
            }
        }
    }
}