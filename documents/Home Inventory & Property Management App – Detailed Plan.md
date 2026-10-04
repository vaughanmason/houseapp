## Home Inventory & Property Management App – Detailed Plan  
## Vision  
Build a **digital twin of your home**.  
The app should answer questions like:  
* What paint is on the guest bathroom wall?  
* Where is my drill stored?  
* When does the geyser need servicing?  
* Which room has oak flooring?  
* Where is the receipt for the dishwasher?  
* Which light bulb fits the hallway?  
* What furniture is in storage?  
* How much is everything in the house worth for insurance?  
The inventory is just one part—the property itself is the primary entity.  
  
⸻  
  
## Core Concepts  
```
Property
    Floors
        Rooms
            Surfaces
            Fixtures
            Utilities
            Assets
            Maintenance
            Photos

```
Everything belongs somewhere.  

Implementation status (2026-10-04): Modules 1–5, 8 (Documents, except OCR), 9 (Photos), 10 (Maintenance), 11 (Utilities) and 12 (Paint Library) are implemented. Modules 6, 7, 15 and 17 are partially implemented. Modules 13, 14 and 16 are not started. See [OUTSTANDING_WORK.md](OUTSTANDING_WORK.md) for details and known bugs.  
  
⸻  
  
## Module 1 – Properties  
Status: Implemented. Property CRUD, photo metadata, and property-scoped relationships are now available in the API, UI, tests, and documentation.
Support multiple properties.  
Examples  
```
Home
Holiday House
Rental Apartment
Parents' House

```
Fields  
* Name  
* Address  
* Purchase Date  
* Purchase Price  
* Floor Area  
* Notes  
* Photos  
  
⸻  
  
## Module 2 – Floors  
Status: Implemented. Floors are now created under properties, exposed through the API/UI, and backed by EF migrations.
```
Ground Floor
First Floor
Basement
Garage

```
  
⸻  
  
## Module 3 – Rooms  
Status: Implemented. Rooms now belong to floors, support the documented metadata, and are available through the API/UI with related paint assignments.
Every room stores permanent information.  
Example  
```
Kitchen

Length
Width
Height

Flooring
Wall Finish
Ceiling Finish

Paint

Photos

Windows

Doors

```
**Room Metadata**  
* Name  
* Type  
* Area  
* Volume  
* Ceiling Height  
  
⸻  
  
## Module 4 – Surfaces  
Status: Implemented. Surfaces are now attached to rooms with type-based metadata and can be managed in the UI and API.
## Walls  
Store  
* Paint Brand  
* Colour  
* Colour Code  
* Finish  
* Number of coats  
* Date painted  
* Painter  
* Quantity purchased  
Example  
```
Wall North

Dulux
Oyster White
Eggshell
Painted 2025

```
  
⸻  
  
## Ceiling  
Separate from walls.  
  
⸻  
  
## Trim  
Store  
* Colour  
* Finish  
  
⸻  
  
## Flooring  
Support  
* Tile  
* Vinyl  
* Carpet  
* Concrete  
* Hardwood  
* Laminate  
Store  
Manufacturer  
Product Name  
Colour  
Supplier  
Warranty  
Invoice  
Installation Date  
  
⸻  
  
## Module 5 – Fixtures  
Status: Implemented. Room-scoped fixtures with manufacturer/model/serial, purchase and value, warranty, manual URL, installer, maintenance schedule, condition and photo metadata are available in the API, UI, import/export and tests.
Permanent items.  
Examples  
* Sink  
* Toilet  
* Shower  
* Bath  
* Built-in cupboards  
* Windows  
* Doors  
* Air conditioner  
* Ceiling fan  
* Alarm  
* Solar inverter  
Each stores  
Manufacturer  
Model  
Serial Number  
Purchase Date  
Warranty  
Manual  
Installer  
Maintenance Schedule  
  
⸻  
  
## Module 6 – Home Inventory  
Movable items.  
Examples  
Furniture  
Electronics  
Kitchen  
Garage  
Garden  
Office  
Bedroom  
Storage  
Fields  
Name  
Description  
Brand  
Model  
Serial Number  
Purchase Price  
Current Value  
Purchase Date  
Condition  
Location  
Photos  
Barcode  
QR Code  
Receipt  
Manual  
Warranty  
Notes  
  
⸻  
  
## Module 7 – Storage  
Instead of just rooms:  
```
Garage

Shelf A

Shelf B

Drawer 1

Cabinet 4

Storage Box 12

```
Then searching  
```
Passport Scanner

↓

Garage
Shelf B
Box 12

```
  
⸻  
  
## Module 8 – Documents  
Attach documents anywhere.  
Examples  
Invoices  
Insurance  
Manuals  
Certificates  
Plans  
Warranties  
Store  
PDF  
Image  
Notes  
Tags  
OCR text  
  
⸻  
  
## Module 9 – Photos  
Every object can have unlimited photos.  
Examples  
Before renovation  
After renovation  
Installation  
Damage  
Receipts  
  
⸻  
  
## Module 10 – Maintenance  
Status: Implemented. Tasks on a property or fixture, one-off or recurring (days/months/years), service history with cost and supplier, automatic next-due dates, dashboard counts and backup support. Photos on service records come with the uploads module.
Track recurring maintenance.  
Examples  
```
Air Conditioner

Every 12 months
Pool Pump

Every 6 months
Roof Inspection

Every year

```
Store  
Due Date  
Last Service  
Supplier  
Cost  
Notes  
Photos  
  
⸻  
  
## Module 11 – Utilities  
Track  
Electrical Panel  
Water Meter  
Gas Meter  
Solar  
Battery  
Inverter  
Generator  
Internet  
Each has  
Manufacturer  
Serial Number  
Documents  
Maintenance  
  
⸻  
  
## Module 12 – Paint Library  
Don’t duplicate paint.  
Create one paint record.  
```
Paint

Brand

Dulux

Colour

Oyster White

Code

OW-104

Finish

Eggshell

```
Then assign to multiple rooms.  
  
⸻  
  
## Module 13 – Asset History  
Every change creates history.  
TV  
```
Bought

↓

Moved Lounge

↓

Moved Office

↓

Repair

↓

Sold

```
Nothing gets deleted.  
  
⸻  
  
## Module 14 – Insurance  
Automatic totals.  
```
Electronics

R340 000

Furniture

R280 000

Jewellery

R160 000

```
Export PDF.  
  
⸻  
  
## Module 15 – Search  
Search everything.  
Examples  
```
Samsung

```
Returns  
TV  
Phone  
Monitor  
Manual  
Receipt  
Warranty  
  
⸻  
  
Search  
```
Blue Paint

```
Returns  
Guest Bathroom  
Hallway  
Garage  
  
⸻  
  
## Module 16 – QR Codes  
Generate QR codes.  
Stick them on  
Storage boxes  
Cupboards  
Tools  
Equipment  
Scan  
↓  
Open immediately.  
  
⸻  
  
## Module 17 – Dashboard  
Show  
```
12 maintenance tasks due

3 warranties expiring

5 missing receipts

Assets worth R2.8M

Newest purchases

Rooms completed 87%

```
  
⸻  
  
## AI Features  
## Take a Photo  
AI detects  
```
Samsung TV

↓

Brand

↓

Model

↓

Category

↓

Estimated Value

```
  
⸻  
  
## OCR  
Scan receipt.  
Automatically extract  
Store  
Price  
Date  
Warranty  
  
⸻  
  
## Paint Recognition  
Take photo.  
AI estimates  
* Colour  
* Finish  
and links to paint library if it already exists.  
  
⸻  
  
## Ask Questions  
```
Where is my hammer?

```
↓  
Garage  
Cabinet B  
Drawer 2  
  
⸻  
  
```
Which rooms use Oyster White?

```
↓  
Kitchen  
Dining Room  
Hallway  
  
⸻  
  
## Suggested Tech Stack  
## Frontend  
* Flutter (Android, iOS, Windows, macOS, Linux, Web)  
* Material 3  
* Offline-first architecture  
## Backend  
* ++[ASP.NET](http://asp.net/)++ Core Minimal APIs  
* Clean Architecture  
* CQRS (optional)  
## Database  
**Local**  
SQLite  
**Cloud Sync (optional)**  
PostgreSQL  
## File Storage  
* Local filesystem for offline use  
* S3-compatible object storage (e.g., MinIO or AWS S3) for cloud sync  
## Authentication  
* Optional local-only mode  
* OAuth (Google, Apple, Microsoft) for cloud accounts  
  
⸻  
  
## High-Level Data Model  
```
Property
├── Floor
│   ├── Room
│   │   ├── Surface
│   │   ├── Fixture
│   │   ├── Asset
│   │   ├── Utility
│   │   └── Photo
│   ├── StorageLocation
│   └── MaintenanceTask
├── Paint
├── Document
├── Contact (Supplier/Installer)
├── Manufacturer
├── Warranty
└── Category

```
  
⸻  
  
## Future Enhancements  
* Multi-user households with role-based permissions  
* Shared shopping and replacement lists  
* Renovation planning and budgeting  
* Home valuation and depreciation tracking  
* Integration with smart home platforms (Home Assistant, Matter)  
* Barcode scanning for products  
* NFC tags for fixtures and storage  
* Public API and webhooks  
* Import/export (CSV, JSON, HomeBox compatibility)  
* AI-powered maintenance recommendations and duplicate detection  
This design provides a solid foundation for an offline-first application while remaining extensible enough to support cloud synchronization, collaboration, and advanced AI features in later releases.  
