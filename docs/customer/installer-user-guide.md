# PageMaker365 Installer User Guide

Status: controlled draft; not approved for customer publication

Tracking issue: [#11](https://github.com/cloudbossdev/pagemaker365-installer/issues/11)

## Purpose

This guide will help an authorized customer operator install, validate, remove, and reinstall PageMaker365 through the Windows desktop installer without running raw deployment scripts.

The current alpha provisions Azure resources but does not yet deploy the PageMaker365 API and portal application content. A live installation cannot be represented as complete until issue [#5](https://github.com/cloudbossdev/pagemaker365-installer/issues/5) is resolved and deployment-bound validation passes.

## Publication Outline

1. Supported workflows and limitations
2. Roles and information to prepare
3. Obtain the PageMaker365 setup file
4. Start a new session or resume safely
5. Load and verify the customer package
6. Sign in to Azure and Microsoft Graph
7. Run preflight and resolve blockers
8. Review the deployment preview
9. Approve and run installation
10. Validate the deployed application
11. Open the verified customer URL and finish
12. Retry portal synchronization
13. Diagnose warnings and create a support bundle
14. Remove PageMaker365 Azure resources
15. Reinstall with a new package and Key Vault name
16. Frequently asked questions

## Customer Experience Requirements

- The standard path asks the customer to choose one PageMaker365 setup file.
- Package generation and download occur inside the Package step.
- Both required sign-ins must complete before dependent checks can advance.
- Long-running actions show an active progress state and cannot be started twice.
- Blockers explain what failed, why progression stopped, and the next corrective action.
- Restarting the app does not restore tokens, secrets, or destructive approval.
- Successful validation displays the verified PageMaker365 customer URL.
- Removal never deletes SharePoint content or purges Key Vault.
- An authorized removal session reports sanitized progress to the portal; a sync warning can be retried without rerunning Azure deletion.

## Content Still Required Before Publication

- Released installer screenshots with synthetic data.
- Final customer roles and permission prerequisites from issue #8.
- Verified runtime completion and customer URL behavior from issue #5.
- Supported upgrade instructions from issue #6.
- Signed distribution and verification instructions from issue #13.
- Clean-operator walkthrough results from issue #10.

Until these items are complete, `docs/using-the-installer.md` remains the engineering operator reference rather than a customer publication.
