using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DatingApp.API.Data;
using DatingApp.API.Dto;
using DatingApp.API.Helpers;
using DatingApp.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DatingApp.API.Controllers
{
    [Authorize]
    [Route("api/users/{userId}/photos")]
    public class PhotosController : ControllerBase
    {
        private readonly IDatingRepository _repo;
        private readonly IMapper _mapper;
        private readonly IOptions<CloudinarySettings> _cloudinaryConfig;
        private readonly Cloudinary _cloudinary;

        public PhotosController(IDatingRepository repo,
                                IMapper mapper,
                                IOptions<CloudinarySettings> cloudinaryConfig)
        {
            _repo = repo;
            _mapper = mapper;
            _cloudinaryConfig = cloudinaryConfig;

            Account acct = new Account(
                _cloudinaryConfig.Value.CloudName,
                _cloudinaryConfig.Value.ApiKey,
                _cloudinaryConfig.Value.ApiSecret
            );
            _cloudinary = new Cloudinary(acct);
        }
        [HttpGet("{id}", Name = "GetPhoto")]
        public async Task<IActionResult> GetPhoto(int id)
        {
            var photoFromRepo = await _repo.GetPhoto(id);
            var photo = _mapper.Map<PhotoForReturnDto>(photoFromRepo);
            return Ok(photo);
        }

        [HttpPost]
        public async Task<IActionResult> AddPhotoForUser(int userId, PhotoForCreationDto photoDto)
        {
            var user = await _repo.GetUser(userId);
            if (user == null)
                return BadRequest("Could not find user");

            var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            if (currentUserId != user.Id)
                return Unauthorized();
            var file = photoDto.File;
            var uploadResult = new ImageUploadResult();
            if (file.Length > 0)
            {
                using (var stream = file.OpenReadStream())
                {
                    var uploadParams = new ImageUploadParams()
                    {
                        File = new FileDescription(file.Name, stream),
                        Transformation = new Transformation().Width(500).Height(500).Crop("fill").Gravity("face")
                    };
                    uploadResult = _cloudinary.Upload(uploadParams);
                }
            }
            photoDto.Url = uploadResult.Uri.ToString();
            photoDto.PublicId = uploadResult.PublicId;

            //map photo with photo entity
            var photo = _mapper.Map<Photo>(photoDto);
            // set user to photo
            photo.User = user;

            //check if user has main photo
            if (!user.Photos.Any(m => m.IsMain))
                photo.IsMain = true; //set photo to main photo if there is no main photo
            // add photo to db
            user.Photos.Add(photo);            

            if (await _repo.SaveAll())
            {
                var photoToReturn = _mapper.Map<PhotoForReturnDto>(photo);
                return CreatedAtRoute("GetPhoto", new { id = photo.Id }, photoToReturn);
            }
            return BadRequest("Could not add the photo to db");
        }
        [HttpPost("{id}/setMain")]
        public async Task<IActionResult> SetMainPhoto(int userId, int id)
        {
            var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            if (currentUserId != userId)
                return Unauthorized();
            //get photo by id
            var photoFromRepo = await _repo.GetPhoto(id);
            //check if pix is in db
            if (photoFromRepo == null)
                return NotFound();
            //check if is the main photo
            if (photoFromRepo.IsMain)
                return BadRequest("This is already the main photo");
            //if not, get the current main photo
            var currentMainPhoto = await _repo.GetMainPhotoForUser(userId);
            //set the current photo to false
            if (currentMainPhoto != null)
                currentMainPhoto.IsMain = false;
            // change the new photo to true
            photoFromRepo.IsMain = true;
            if (await _repo.SaveAll())
                return NoContent();
            return BadRequest("Could not set photo to Main");
        }
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePhoto(int userId, int id)
        {
            if (userId != int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value))
                return Unauthorized();
            // get and check image by id
            var photoFromRepo = await _repo.GetPhoto(id);
            if (photoFromRepo == null)
                return NotFound();
            //cannot delete main photo
            if (photoFromRepo.IsMain)
                return BadRequest("This is already the main photo");
            // delete from fro cloudinary
            if(photoFromRepo.PublicId != null)
            {
                var deleteParams = new DeletionParams(photoFromRepo.PublicId);
                var result = _cloudinary.Destroy(deleteParams);
                if (result.Result == "ok")
                    _repo.Delete(photoFromRepo);
            }
            // delete photo that is not in the cloudinary
            if(photoFromRepo.PublicId == null)
            {
                _repo.Delete(photoFromRepo);
            }
            
            if (await _repo.SaveAll())
                return Ok();
            return BadRequest("Failed to delete the photo");
        }
    }
}