IF DB_ID('HikkaExplorer') IS NULL CREATE DATABASE HikkaExplorer;
GO
USE HikkaExplorer;
GO
CREATE TABLE Category(CategoryId INT IDENTITY PRIMARY KEY, Name NVARCHAR(50) NOT NULL UNIQUE, Description NVARCHAR(200) NULL);
CREATE TABLE Attraction(
  AttractionId INT IDENTITY PRIMARY KEY, Name NVARCHAR(100) NOT NULL,
  CategoryId INT NOT NULL REFERENCES Category(CategoryId), Description NVARCHAR(1000) NOT NULL,
  Latitude DECIMAL(9,6) NULL, Longitude DECIMAL(9,6) NULL, DistanceKm DECIMAL(5,1) NULL CHECK (DistanceKm>=0),
  OpeningTimes NVARCHAR(200) NULL, EntranceFee NVARCHAR(100) NULL, TravelTips NVARCHAR(500) NULL,
  VisitorInfo NVARCHAR(500) NULL, IsAvailable BIT NOT NULL DEFAULT 1);
CREATE TABLE AttractionImage(ImageId INT IDENTITY PRIMARY KEY, AttractionId INT NOT NULL REFERENCES Attraction(AttractionId) ON DELETE CASCADE, ImagePath NVARCHAR(260) NOT NULL, Caption NVARCHAR(200) NULL);
CREATE TABLE Itinerary(ItineraryId INT IDENTITY PRIMARY KEY, SessionToken UNIQUEIDENTIFIER NOT NULL UNIQUE, CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
CREATE TABLE ItineraryItem(ItemId INT IDENTITY PRIMARY KEY, ItineraryId INT NOT NULL REFERENCES Itinerary(ItineraryId) ON DELETE CASCADE,
  AttractionId INT NOT NULL REFERENCES Attraction(AttractionId), VisitOrder INT NOT NULL, CONSTRAINT UQ_Itin_Attr UNIQUE(ItineraryId,AttractionId));
GO
INSERT Category(Name,Description) VALUES (N'Beach',N'Beaches and coastal recreation'),(N'Nature',N'Natural and marine environments'),
 (N'Historical',N'Heritage and historic sites'),(N'Religious',N'Temples and pilgrimage sites'),(N'Cultural',N'Museums and cultural attractions');
-- DistanceKm = midpoint of the ranges in the project proposal; coordinates are approximate. VERIFY, then fill NULL fields (hours, fees, tips) from official sources.
INSERT Attraction(Name,CategoryId,Description,Latitude,Longitude,DistanceKm) VALUES
(N'Hikkaduwa Beach',1,N'A popular coastal attraction known for beach activities, surfing, swimming, snorkelling, and diving.',6.1407,80.0998,1),
(N'Hikkaduwa Coral Sanctuary',2,N'A protected coral reef area with marine life and colourful coral formations for observing underwater ecosystems.',6.1380,80.0970,2),
(N'Seenigama Devalaya',4,N'A culturally important religious site near the coast, associated with local traditions and pilgrimage.',6.1190,80.1130,3.5),
(N'Ariyapala Traditional Masks Museum',5,N'A cultural attraction in Ambalangoda about Sri Lanka''s traditional mask-making heritage.',6.2360,80.0530,12),
(N'Galle Fort',3,N'A historic fortified city with Portuguese and Dutch colonial heritage, including ramparts, historic buildings and museums.',6.0268,80.2168,18),
(N'Galle Lighthouse',3,N'A prominent landmark within Galle Fort overlooking the Indian Ocean.',6.0246,80.2155,18.5),
(N'Unawatuna Beach',1,N'A popular coastal destination suitable for beach activities, swimming, relaxation and sightseeing.',6.0100,80.2495,21),
(N'Japanese Peace Pagoda, Rumassala',4,N'A Buddhist peace pagoda near Unawatuna offering scenic views of the surrounding coast.',6.0161,80.2420,22),
(N'Jungle Beach, Rumassala',1,N'A relatively secluded beach surrounded by natural vegetation near the Rumassala area.',6.0130,80.2470,23),
(N'Koggala Lake',2,N'A scenic coastal lagoon known for its natural environment, waterways and recreational experiences.',5.9960,80.3000,24.5);
GO
